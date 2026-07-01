# 🛠️ Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# ก๊อปปี้ไฟล์ .csproj เข้าไปก่อนเพื่อ Restore Dependencies (ใช้ Cache ของ Docker ให้เป็นประโยชน์)
COPY ["TicketProjectionWorker.csproj", "./"]
RUN dotnet restore "./TicketProjectionWorker.csproj"

# ก๊อปปี้โค้ดทั้งหมด (Program.cs) ตามเข้าไป
COPY . .
RUN dotnet publish "TicketProjectionWorker.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 🚀 Stage 2: Production Image (ใช้แค่ Runtime)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# พอร์ตพื้นฐานที่ Minimal API จะรัน (ตรงกับ ASPNETCORE_HTTP_PORTS=8080 ใน docker-compose)
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TicketProjectionWorker.dll"]