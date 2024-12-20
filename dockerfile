# Etapa de construcción
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copiar solo el archivo de solución y restaurar las dependencias
COPY tuvendedorback.sln .
COPY tuvendedorback/tuvendedorback.csproj ./tuvendedorback/

RUN dotnet restore tuvendedorback.sln

# Copiar el resto de los archivos y compilar la aplicación
COPY . .
RUN dotnet publish -c Release -o out

# Etapa de ejecución
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copiar la aplicación compilada desde la etapa de build
COPY --from=build /app/out .

# Copiar las fuentes locales al contenedor
COPY ./tuvendedorback/assets/fonts /app/assets/fonts

# Configurar ASP.NET Core para escuchar en el puerto 80
ENV ASPNETCORE_URLS=http://+:80

# Exponer los puertos 80 (HTTP) y 443 (HTTPS)
EXPOSE 80

ENTRYPOINT ["dotnet", "tuvendedorback.dll"]
