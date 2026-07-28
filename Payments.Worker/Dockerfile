# Stage 1: Build and publish the Worker
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy the project file first and restore NuGet packages
COPY ["Payments.Worker/Payments.Worker.csproj", "Payments.Worker/"]
RUN dotnet restore "Payments.Worker/Payments.Worker.csproj"

# Copy the remaining repository files
COPY . .

WORKDIR "/src/Payments.Worker"

RUN dotnet publish "Payments.Worker.csproj" \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false


# Stage 2: Create the smaller production image
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Payments.Worker.dll"]