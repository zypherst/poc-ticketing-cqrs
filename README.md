🎬 ฉากที่ 1: ฝั่งรับคำสั่ง (The Write Path)
1. Frontend ➡️ Backend API (ส่งคำสั่งซื้อ)

ใครคุยกับใคร: หน้าเว็บ React คุยกับ .NET Backend API (ผ่าน Controller UpdateSeatStatus)

คุยยังไง: ผ่าน HTTP POST Request

ส่งอะไรไป: ส่ง JSON Payload บอกว่า รอบฉายอะไร, เก้าอี้เบอร์อะไรบ้าง, และต้องการเปลี่ยนเป็นสถานะอะไร (เช่น Paid)

2. Backend API ➡️ PostgreSQL (ยืนยันความถูกต้องและบันทึก)

ใครคุยกับใคร: .NET Backend คุยกับฐานข้อมูล PostgreSQL โดยมี Dapr (sqldb binding) เป็นตัวกลาง

คุยยังไง: ส่งคำสั่ง SQL ไปทำงานแบบ Transaction เดียวรวด (ใช้บล็อก DO $$BEGIN ... END$$;)

ส่งอะไรไป/ทำอะไร: * ทำ Optimistic Concurrency Control (OCC): เช็คก่อนว่าเก้าอี้ตัวนั้นยังสถานะเดิมอยู่ไหม (มีใครแย่งไปหรือเปล่า)

อัปเดตสถานะเก้าอี้ลงตาราง seats

สร้างบันทึกข้อความ (Event) แปะไว้ในตาราง outbox_events
(เมื่อทำขั้นตอนนี้จบ API จะตอบกลับ Frontend ว่าสำเร็จทันที ถือว่าจบหน้าที่ของ API หลักแล้วครับ)

🎬 ฉากที่ 2: ฝั่งกระจายข่าว (The Event Distribution)
3. PostgreSQL ➡️ Sequin (บุรุษไปรษณีย์แอบดู)

ใครคุยกับใคร: เครื่องมือ CDC (Sequin) คอยเฝ้าดูความเปลี่ยนแปลงในตาราง outbox_events ของ PostgreSQL

คุยยังไง: ผ่านระบบ Logical Replication (จำข้อมูลเก่าตอนระบบล่มได้)

ส่งอะไรไป: ทันทีที่มีแถวใหม่เพิ่มใน Outbox ตัว Sequin จะดูดข้อมูล JSON Payload ของเก้าอี้ตัวนั้นออกมา

4. Sequin ➡️ NATS JetStream (ประกาศออกไมค์)

ใครคุยกับใคร: Sequin นำข้อความที่ดูดมาไปส่งให้ Message Broker (NATS) ในห้องส่งที่ชื่อว่า Stream SEAT_STREAM

คุยยังไง: ส่งเข้าหัวข้อ (Subject) ที่ชื่อว่า seat.events

ส่งอะไรไป: ส่ง Raw JSON ที่บอกว่า "ประกาศๆ เก้าอี้ A1 ของรอบฉาย 1 ถูกเปลี่ยนสถานะเป็น Paid แล้ว!"

🎬 ฉากที่ 3: ฝั่งอัปเดตผังที่นั่ง (The Read Model Projection)
5. NATS ➡️ Projection Worker (มดงานรับข่าว)

ใครคุยกับใคร: NATS คอยกระจายข่าวให้ .NET Worker ผ่านตัวกลางคือ Dapr Pub/Sub (ที่มีกลไกรับประกันการส่ง ถ้าตายก็เก็บไว้ให้)

คุยยังไง: Dapr ยิง HTTP POST แบบ CloudEvent ห่อ Raw JSON เข้ามาที่ Endpoint /process-seat-event ของ Worker

ทำอะไร: Worker แกะกล่องข้อมูล เอาไอดีรอบฉายและเบอร์เก้าอี้ออกมา

6. Projection Worker ➡️ NATS KV Store (อัปเดตป้ายประกาศ)

ใครคุยกับใคร: Worker หันไปคุยกับ NATS KV (กระดานข้อมูลที่อ่านได้เร็วมาก)

คุยยังไง: ผ่าน Dapr State Management (GetStateAsync และ SaveStateAsync)

ส่งอะไรไป/ทำอะไร: * ดึงผังที่นั่งล่าสุดของรอบฉายนั้นมากางออกใน Memory

ใช้ท่า Delta Update: เอาพู่กันป้ายสีเก้าอี้ตัวที่เปลี่ยนสถานะให้กลายเป็นตัวใหม่ (เช่น เปลี่ยนเป็น Paid)

แปะผังที่นั่งฉบับใหม่นี้กลับลงไปทับใน NATS KV ทันที

✨ บทสรุป (The Read Path):
เมื่อผู้ใช้คนอื่นๆ กดรีเฟรชหน้าเว็บ Backend ของคุณก็แค่เดินไปหยิบข้อมูลที่ Worker แปะทิ้งไว้ใน NATS KV ส่งกลับไปให้ Frontend ทันที (ซึ่งใช้เวลาไม่ถึง 10-20 มิลลิวินาที) โดยไม่ต้องไปกวน PostgreSQL เลยสักนิดเดียวครับ!
