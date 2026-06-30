# Stage 1: Build the React application
FROM node:20-alpine AS build
WORKDIR /app

# Install dependencies
COPY package.json package-lock.json* ./
RUN npm install

# Copy source code and build
COPY . .
RUN npm run build

# Stage 2: Serve with Nginx
FROM nginx:alpine
# Copy built static files to Nginx public folder
COPY --from=build /app/dist /usr/share/nginx/html

# Expose port 80 (จะถูก Map ออกไปที่ 3000 ใน docker-compose.yml)
EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]