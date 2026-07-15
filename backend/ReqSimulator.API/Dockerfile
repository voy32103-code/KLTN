# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj và restore trước để tận dụng cache của Docker
COPY *.csproj ./
RUN dotnet restore

# Copy toàn bộ code và publish ra thư mục /app/publish
COPY . ./
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Cấu hình .NET 9 lắng nghe cổng 8080 (Render sẽ định tuyến tự động qua cổng này)
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ReqSimulator.API.dll"]
