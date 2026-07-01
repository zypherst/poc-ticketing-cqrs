-- 1. สร้างโครงสร้าง Table ที่สัมพันธ์กัน
CREATE TABLE IF NOT EXISTS branches (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL
);

CREATE TABLE IF NOT EXISTS cinemas (
    id SERIAL PRIMARY KEY,
    branch_id INT REFERENCES branches(id),
    name VARCHAR(50) NOT NULL,
    seats_per_row INT NOT NULL -- จำนวนที่นั่งต่อแถว (เช่น 10, 12, 15)
);

CREATE TABLE IF NOT EXISTS showtimes (
    id SERIAL PRIMARY KEY,
    cinema_id INT REFERENCES cinemas(id),
    movie_title VARCHAR(100) NOT NULL,
    show_time TIMESTAMP NOT NULL
);

CREATE TABLE IF NOT EXISTS seats (
    showtime_id INT REFERENCES showtimes(id) ON DELETE CASCADE,
    seat_code VARCHAR(10) NOT NULL, -- เช่น 'A1', 'G15'
    status VARCHAR(20) DEFAULT '',  -- ว่าง = '', ล๊อค = 'Lock', จ่ายแล้ว = 'Paid'
    payment_time TIMESTAMP NULL,
    PRIMARY KEY (showtime_id, seat_code)
);

-- 🆕 1.1 สร้างตาราง Outbox Events สำหรับทำ CQRS (ให้ Sequin มาอ่าน)
CREATE TABLE IF NOT EXISTS outbox_events (
    id SERIAL PRIMARY KEY,
    aggregate_type VARCHAR(100) NOT NULL,
    aggregate_id VARCHAR(100) NOT NULL,
    event_type VARCHAR(100) NOT NULL,
    payload JSONB NOT NULL,
    created_at TIMESTAMP DEFAULT NOW()
);

-- 2. เพิ่มข้อมูลสาขา
INSERT INTO branches (id, name) VALUES 
(1, 'สาขาที่ 1 (พารากอน)'), 
(2, 'สาขาที่ 2 (เซ็นทรัล)')
ON CONFLICT (id) DO NOTHING;

-- 3. เพิ่มข้อมูลโรงภาพยนตร์ (สาขา 1 มี 3 โรง / สาขา 2 มี 4 โรง) พร้อมกำหนด X (ที่นั่งต่อแถว) ไม่เท่ากัน
INSERT INTO cinemas (id, branch_id, name, seats_per_row) VALUES
(1, 1, 'Cinema 1', 10), -- แถว A-G แถวละ 10 ที่นั่ง (70 ที่นั่ง/รอบ)
(2, 1, 'Cinema 2', 12), -- แถว A-G แถวละ 12 ที่นั่ง (84 ที่นั่ง/รอบ)
(3, 1, 'Cinema 3', 15), -- แถว A-G แถวละ 15 ที่นั่ง (105 ที่นั่ง/รอบ)
(4, 2, 'Cinema 1', 10),
(5, 2, 'Cinema 2', 11),
(6, 2, 'Cinema 3', 13),
(7, 2, 'Cinema 4', 14)
ON CONFLICT (id) DO NOTHING;

-- 4. เพิ่มรอบฉาย (สุ่มหนังปี 2021-2024 โรงละ 3 รอบฉาย)
INSERT INTO showtimes (id, cinema_id, movie_title, show_time) VALUES
-- สาขา 1 - โรง 1
(1, 1, 'Spider-Man: No Way Home (2021)', '2026-07-01 11:00:00'),
(2, 1, 'Dune: Part One (2021)', '2026-07-01 14:30:00'),
(3, 1, 'Top Gun: Maverick (2022)', '2026-07-01 18:00:00'),
-- สาขา 1 - โรง 2
(4, 2, 'Avatar: The Way of Water (2022)', '2026-07-01 10:30:00'),
(5, 2, 'The Batman (2022)', '2026-07-01 14:00:00'),
(6, 2, 'Oppenheimer (2023)', '2026-07-01 18:30:00'),
-- สาขา 1 - โรง 3
(7, 3, 'Barbie (2023)', '2026-07-01 11:15:00'),
(8, 3, 'Past Lives (2023)', '2026-07-01 14:00:00'),
(9, 3, 'Dune: Part Two (2024)', '2026-07-01 17:45:00'),
-- สาขา 2 - โรง 1
(10, 4, 'Top Gun: Maverick (2022)', '2026-07-01 11:00:00'),
(11, 4, 'Oppenheimer (2023)', '2026-07-01 15:00:00'),
(12, 4, 'Dune: Part Two (2024)', '2026-07-01 19:00:00'),
-- สาขา 2 - โรง 2
(13, 5, 'Spider-Man: No Way Home (2021)', '2026-07-01 10:00:00'),
(14, 5, 'The Batman (2022)', '2026-07-01 13:30:00'),
(15, 5, 'Barbie (2023)', '2026-07-01 17:00:00'),
-- สาขา 2 - โรง 3
(16, 6, 'Dune: Part One (2021)', '2026-07-01 12:00:00'),
(17, 6, 'Avatar: The Way of Water (2022)', '2026-07-01 16:00:00'),
(18, 6, 'Godzilla x Kong (2024)', '2026-07-01 20:00:00'),
-- สาขา 2 - โรง 4
(19, 7, 'Past Lives (2023)', '2026-07-01 11:30:00'),
(20, 7, 'Inside Out 2 (2024)', '2026-07-01 14:45:00'),
(21, 7, 'Furiosa: A Mad Max Saga (2024)', '2026-07-01 18:15:00')
ON CONFLICT (id) DO NOTHING;

-- 5. สคริปต์อัตโนมัติสร้างที่นั่งแถว A-G คูณจำนวน X ของโรงนั้นๆ ให้ครบทุกรอบฉาย
DO $$
DECLARE
    row_record RECORD;
    st_record RECORD;
    row_char CHAR(1);
    seat_num INT;
    seat_code_var VARCHAR(10);
BEGIN
    -- วนลูปทุกรอบฉายที่สร้างไว้
    FOR st_record IN SELECT id, cinema_id FROM showtimes LOOP
        -- ดึงจำนวนที่นั่งต่อแถวของโรงภาพยนตร์นั้นมาคํานวณ
        SELECT seats_per_row INTO seat_num FROM cinemas WHERE id = st_record.cinema_id;
        
        -- วนลูปสร้างแถว A ถึง G
        FOREACH row_char IN ARRAY ARRAY['A', 'B', 'C', 'D', 'E', 'F', 'G'] LOOP
            -- วนลูปสร้างเลขเก้าอี้ 1 ถึง X
            FOR i IN 1..seat_num LOOP
                seat_code_var := row_char || i;
                
                INSERT INTO seats (showtime_id, seat_code, status, payment_time)
                VALUES (st_record.id, seat_code_var, '', NULL)
                ON CONFLICT (showtime_id, seat_code) DO NOTHING;
            END LOOP;
        END LOOP;
    END LOOP;
END $$;

-- 6. 🆕 เปิดช่องทาง Logical Replication ให้ Sequin
-- สร้าง Publication เพื่อประกาศให้ Sequin รู้ว่ามีตารางอะไรบ้าง
CREATE PUBLICATION sequin_pub FOR ALL TABLES WITH (publish_via_partition_root = true);

-- สร้าง Replication Slot เพื่อให้ Sequin มาเกาะอ่านข้อมูล
SELECT pg_create_logical_replication_slot('sequin_slot', 'pgoutput');

-- 🛠️ ตั้งค่าให้ตาราง outbox_events ส่งข้อมูลครบถ้วนเมื่อมีการเปลี่ยนแปลง (สำคัญสำหรับ CDC)
ALTER TABLE "public"."outbox_events" REPLICA IDENTITY FULL;