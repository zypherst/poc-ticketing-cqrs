# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["TicketBookingPoc.csproj", "./"]
RUN dotnet restore "TicketBookingPoc.csproj"

# Copy the rest of the code and build
COPY . .
RUN dotnet publish "TicketBookingPoc.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app
EXPOSE 8080

RUN apk add --no-cache tzdata
ENV TZ=Asia/Bangkok

# Copy output from build stage
COPY --from=build /app/publish .

# Start the application
ENTRYPOINT ["dotnet", "TicketBookingPoc.dll"]