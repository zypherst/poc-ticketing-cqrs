-- 1. ตารางโรงภาพยนตร์ (คงเดิม)
CREATE TABLE cinemas (
    id SERIAL PRIMARY KEY,
    branch_id INT NOT NULL,
    name VARCHAR(100) NOT NULL,
    seats_per_row INT NOT NULL
);

-- 2. ตารางรอบฉาย (คงเดิม)
CREATE TABLE showtimes (
    id SERIAL PRIMARY KEY,
    cinema_id INT REFERENCES cinemas(id),
    movie_title VARCHAR(200) NOT NULL,
    show_time TIMESTAMP NOT NULL
);

-- 3. ตารางที่นั่ง (🆕 เพิ่ม locked_at)
CREATE TABLE seats (
    showtime_id INT REFERENCES showtimes(id),
    seat_code VARCHAR(10),
    status VARCHAR(20),       -- '', 'Lock', 'Paid'
    payment_time TIMESTAMP,
    locked_at TIMESTAMP,      -- 🆕 เวลาที่เริ่ม Lock เพื่อใช้เช็คหมดอายุ 1 นาที
    PRIMARY KEY (showtime_id, seat_code)
);

-- 4. 🆕 ตาราง Outbox สำหรับ Eventual Consistency
CREATE TABLE outbox_events (
    id SERIAL PRIMARY KEY,
    aggregate_type VARCHAR(50) NOT NULL, -- เช่น 'Seat'
    aggregate_id VARCHAR(50) NOT NULL,   -- เช่น '1-A1' (showtimeId-seatCode)
    event_type VARCHAR(50) NOT NULL,     -- เช่น 'SeatLocked', 'SeatPaid', 'SeatReleased'
    payload JSONB NOT NULL,              -- ข้อมูลสถานะใหม่ (ส่งให้ Sequin -> NATS)
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- แทรกข้อมูลสาขาและรอบฉายจำลอง (เหมือนเดิม)
INSERT INTO cinemas (branch_id, name, seats_per_row) VALUES 
(1, 'Cinema 1', 10),
(1, 'Cinema 2', 15),
(2, 'Cinema 1', 12);

INSERT INTO showtimes (cinema_id, movie_title, show_time) VALUES 
(1, 'The Matrix', '2026-07-01 10:00:00'),
(1, 'Inception', '2026-07-01 13:00:00'),
(2, 'Interstellar', '2026-07-01 15:00:00'),
(2, 'Avatar', '2026-07-01 18:00:00'),
(3, 'Avengers', '2026-07-01 20:00:00');

-- สคริปต์วนลูปสร้างเก้าอี้
DO $$
DECLARE
    row_record RECORD;
    st_record RECORD;
    row_char CHAR(1);
    seat_num INT;
    seat_code_var VARCHAR(10);
BEGIN
    FOR st_record IN SELECT id, cinema_id FROM showtimes LOOP
        SELECT seats_per_row INTO seat_num FROM cinemas WHERE id = st_record.cinema_id;
        
        FOREACH row_char IN ARRAY ARRAY['A', 'B', 'C', 'D', 'E', 'F', 'G'] LOOP
            FOR i IN 1..seat_num LOOP
                seat_code_var := row_char || i;
                
                -- แทรกข้อมูลที่นั่งตั้งต้น (สถานะว่าง)
                INSERT INTO seats (showtime_id, seat_code, status, payment_time, locked_at)
                VALUES (st_record.id, seat_code_var, '', NULL, NULL)
                ON CONFLICT (showtime_id, seat_code) DO NOTHING;
            END LOOP;
        END LOOP;
    END LOOP;
END $$;

-- 5. 🆕 เปิดช่องทาง Logical Replication ให้ Sequin
-- สร้าง Publication เพื่อประกาศให้ Sequin รู้ว่ามีตารางอะไรบ้าง
CREATE PUBLICATION sequin_pub FOR ALL TABLES WITH (publish_via_partition_root = true);

-- สร้าง Replication Slot เพื่อให้ Sequin มาเกาะอ่านข้อมูล
SELECT pg_create_logical_replication_slot('sequin_slot', 'pgoutput');
ALTER TABLE "public"."outbox_events" REPLICA IDENTITY FULL;