# --- build stage: full SDK, produces the published app -----------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, in its own layer: as long as project files do not change,
# Docker reuses the cached NuGet packages and rebuilds are fast.
COPY Directory.Build.props Torque.slnx .editorconfig ./
COPY src/Torque.Domain/Torque.Domain.csproj src/Torque.Domain/
COPY src/Torque.Application/Torque.Application.csproj src/Torque.Application/
COPY src/Torque.Infrastructure/Torque.Infrastructure.csproj src/Torque.Infrastructure/
COPY src/Torque.Api/Torque.Api.csproj src/Torque.Api/
RUN dotnet restore src/Torque.Api/Torque.Api.csproj

COPY src/ src/
RUN dotnet publish src/Torque.Api/Torque.Api.csproj -c Release -o /app --no-restore

# --- runtime stage: only the ASP.NET runtime, no SDK, no source --------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# The image ships a non-root "app" user; never run a web server as root.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Torque.Api.dll"]
