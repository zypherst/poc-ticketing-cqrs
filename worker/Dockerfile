# Stage 1: Build
FROM golang:1.26-alpine AS builder
RUN apk add --no-cache git
WORKDIR /app

# คัดลอกแค่ไฟล์โค้ด
COPY main.go ./

# สร้าง module และ get library โดยไม่บังคับว่าต้องมี go.sum
RUN go mod init seat-worker && \
    go get github.com/nats-io/nats.go && \
    go mod tidy && \
    CGO_ENABLED=0 GOOS=linux go build -o seat-worker main.go

# Stage 2: Minimal Runtime
FROM scratch
COPY --from=builder /app/seat-worker /seat-worker
ENTRYPOINT ["/seat-worker"]