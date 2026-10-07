# Repo KÖKÜNDEN build edilir; Directory.Build.props ve Directory.Packages.props
# lazım (merkezi versiyonlama). compose'da context: . olarak veriliyor.
#   docker build -f fakes/Sms.Fake/Dockerfile .
#
# Başka bir kurumun yerinde duruyor ve canlıda çalışmaz. MIGRATOR AŞAMASI YOK:
# veritabanı yok.

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props nuget.config ./
COPY fakes/Sms.Fake/Sms.Fake.csproj fakes/Sms.Fake/
COPY src/shared/Shared.Contracts/Shared.Contracts.csproj src/shared/Shared.Contracts/
COPY src/shared/Shared.Infrastructure/Shared.Infrastructure.csproj src/shared/Shared.Infrastructure/
RUN dotnet restore fakes/Sms.Fake/Sms.Fake.csproj

COPY src/ src/
COPY fakes/ fakes/
RUN dotnet publish fakes/Sms.Fake/Sms.Fake.csproj \
        -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

USER app

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "HiWallet.Sms.Fake.dll"]
