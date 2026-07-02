package main

import (
	"context"
	"encoding/json"
	"log"
	"os"
	"time"

	"github.com/nats-io/nats.go"
	"github.com/nats-io/nats.go/jetstream"
)

type SeatEvent struct {
	ID     string `json:"seat_id"`
	Status string `json:"status"`
}

func main() {
	// 1. อ่าน Config
	natsURL := getEnv("NATS_URL", "nats://nats-1:4222,nats://nats-2:4222,nats://nats-3:4222")
	
	// 2. เชื่อมต่อ NATS
	nc, err := nats.Connect(natsURL, nats.MaxReconnects(-1))
	if err != nil {
		log.Fatalf("Failed to connect to NATS: %v", err)
	}
	defer nc.Close()

	js, _ := jetstream.New(nc)
	ctx := context.Background()

	// 3. เตรียม Stream & KV
	stream, _ := js.Stream(ctx, "SEAT_STREAM")
	kv, _ := js.KeyValue(ctx, "seatKV")

	// 4. สร้าง Consumer แบบ Pull (Load balancing อัตโนมัติด้วย Durable Name)
	cons, _ := stream.CreateOrUpdateConsumer(ctx, jetstream.ConsumerConfig{
		Durable:   "seat-updater-group",
		AckPolicy: jetstream.AckExplicitPolicy,
	})

	log.Println("Worker ready. Processing events...")

	for {
		// ดึงข้อความทีละ 1 (Pull)
		msgs, err := cons.Fetch(1)
		if err != nil {
			time.Sleep(1 * time.Second)
			continue
		}

		for msg := range msgs.Messages() { // <--- msg ถูกประกาศที่นี่ (Scope: ใน Loop)
    var event SeatEvent           // <--- event ถูกประกาศที่นี่ (Scope: ใน Loop)
    if err := json.Unmarshal(msg.Data(), &event); err != nil {
// Debug: พิมพ์ JSON ที่ได้ออกมาดู
    log.Printf("ได้รับ Event ดิบ: %+v", event)

    // ป้องกัน Key ว่างและจัดการช่องว่าง
    cleanKey := strings.TrimSpace(event.ID)
    
    if cleanKey == "" {
        log.Printf("⚠️ พบข้อมูลขยะ! ข้อมูลที่ได้รับคือ ID: '%s', Status: '%s'", event.ID, event.Status)
        msg.Term() // ทิ้ง Event นี้ไปเลยเพราะใช้ไม่ได้
        continue
    }
    }

    // ตอนนี้คุณสามารถใช้ msg และ event ได้แล้วตรงนี้
    log.Printf("กำลังประมวลผล Key: %s", event.ID) 

    data, _ := json.Marshal(event)
    _, err = kv.Put(ctx, cleanKey, data)
    
    if err != nil {
        // ตรงนี้แหละครับที่จะบอก Key ที่พัง!
        log.Printf("KV Update failed: %v | Key: %s", err, event.ID) 
        msg.NakWithDelay(5 * time.Second)
        continue
    }

    msg.Ack()
}
	}
}

func getEnv(key, fallback string) string {
	if value, ok := os.LookupEnv(key); ok { return value }
	return fallback
}