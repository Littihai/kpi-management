FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["KPI.API/KPI.API.csproj", "KPI.API/"]
COPY ["KPI.Domain/KPI.Domain.csproj", "KPI.Domain/"]
COPY ["KPI.Application/KPI.Application.csproj", "KPI.Application/"]
COPY ["KPI.Infrastructure/KPI.Infrastructure.csproj", "KPI.Infrastructure/"]

RUN dotnet restore "KPI.API/KPI.API.csproj"

COPY . .
RUN dotnet publish "KPI.API/KPI.API.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "KPI.API.dll"]