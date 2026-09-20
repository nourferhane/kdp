# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Zunavio.KdpFactory.sln ./
COPY src/Zunavio.KdpFactory.Domain/ src/Zunavio.KdpFactory.Domain/
COPY src/Zunavio.KdpFactory.Application/ src/Zunavio.KdpFactory.Application/
COPY src/Zunavio.KdpFactory.Infrastructure/ src/Zunavio.KdpFactory.Infrastructure/
COPY src/Zunavio.KdpFactory.Web/ src/Zunavio.KdpFactory.Web/

RUN dotnet restore src/Zunavio.KdpFactory.Web/Zunavio.KdpFactory.Web.csproj
RUN dotnet publish src/Zunavio.KdpFactory.Web/Zunavio.KdpFactory.Web.csproj \
    -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "Zunavio.KdpFactory.Web.dll"]