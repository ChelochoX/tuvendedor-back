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

# Copiar las imágenes locales desde tu máquina al contenedor
COPY ImagenesMotos /app/ImagenesMotos

# Exponer los puertos 80 (HTTP) y 443 (HTTPS)
EXPOSE 80

ENTRYPOINT ["dotnet", "tuvendedorback.dll"]
