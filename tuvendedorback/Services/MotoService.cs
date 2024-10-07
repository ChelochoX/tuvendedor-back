﻿using Azure.Core;
using tuvendedorback.DTOs;
using tuvendedorback.Models;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class MotoService : IMotoService
{
    private readonly string _baseImagesPath;
    private readonly ILogger<MotoService> _logger;
    private readonly IMotoRepository _repository;
    private readonly IConfiguration _config;

    public MotoService(IConfiguration configuration, ILogger<MotoService> logger, IMotoRepository repository, IConfiguration config)
    {
        _baseImagesPath = configuration["ImagenesMotosPath"];
        _logger = logger;
        _repository = repository;
        _config = config;
    }

    public async Task<List<ModeloMotosporCategoria>> ObtenerModelosPorCategoriaAsync(string categoria)
    {
        if (string.IsNullOrEmpty(_baseImagesPath) || string.IsNullOrEmpty(categoria))
        {
            throw new ArgumentNullException("El valor de la ruta base o la categoría es nulo");
        }

        var rutaBase = _baseImagesPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var categoriaPath = Path.Combine(rutaBase, categoria);

        _logger.LogInformation($"Ruta base: {rutaBase}");
        _logger.LogInformation($"Categoría: {categoria}");
        _logger.LogInformation($"Ruta completa de la categoría: {categoriaPath}");

        if (!Directory.Exists(categoriaPath))
        {
            _logger.LogInformation($"No se encontró la carpeta en la ruta: {categoriaPath}");
            return null; // Retornar null si la categoría no existe
        }

        var subCarpetas = Directory.GetDirectories(categoriaPath);

        if (subCarpetas.Length == 0)
        {
            _logger.LogInformation("No se encontraron subcarpetas dentro de la categoría.");
            return null; // Retornar null si no hay subcarpetas
        }

        var modelos = new List<ModeloMotosporCategoria>();

        foreach (var subCarpeta in subCarpetas)
        {
            var nombreModelo = Path.GetFileName(subCarpeta);
            _logger.LogInformation($"Procesando modelo: {nombreModelo} en la subcarpeta: {subCarpeta}");

            // Obtener la lista de todas las imágenes del modelo
            var imagenes = ObtenerImagenesDeModelo(subCarpeta, categoria);

            if (imagenes == null || imagenes.Count == 0)
            {
                _logger.LogInformation($"No se encontraron imágenes para el modelo: {nombreModelo}");
            }
            else
            {
                _logger.LogInformation($"Se encontraron imágenes para el modelo: {nombreModelo}, URLs: {string.Join(", ", imagenes)}");
            }

            modelos.Add(new ModeloMotosporCategoria
            {
                Nombre = nombreModelo,
                Imagenes = imagenes // Asignar la lista de imágenes al modelo
            });
        }

        if (modelos.Count == 0)
        {
            _logger.LogInformation("No se encontraron modelos con imágenes.");
            return null;
        }

        return modelos;
    }

    // Método para obtener la primera imagen dentro de una carpeta de modelo
    private List<string> ObtenerImagenesDeModelo(string modeloPath, string categoria)
    {
        var extensionesPermitidas = new[] { ".jpg", ".jpeg", ".png" };
        var archivos = Directory.GetFiles(modeloPath)
                                .Where(archivo => extensionesPermitidas.Contains(Path.GetExtension(archivo).ToLower()));

        var listaDeImagenes = new List<string>();

        foreach (var archivo in archivos)
        {
            var nombreArchivo = Path.GetFileName(archivo);

            // Obtener el nombre del modelo (nombre de la carpeta) y reemplazar caracteres especiales y espacios
            var nombreModelo = Path.GetFileName(modeloPath);
            var nombreCategoria = categoria;

            // Codificar los nombres de las carpetas y archivos para que sean válidos en una URL
            var nombreModeloEncoded = Uri.EscapeDataString(nombreModelo);
            var nombreCategoriaEncoded = Uri.EscapeDataString(nombreCategoria);
            var nombreArchivoEncoded = Uri.EscapeDataString(nombreArchivo);

            // Construir la URL codificada correctamente
            var imagenUrl = $"/uploads/{nombreCategoriaEncoded}/{nombreModeloEncoded}/{nombreArchivoEncoded}";

            // Agregar la URL a la lista de imágenes
            listaDeImagenes.Add(imagenUrl);
        }

        return listaDeImagenes;
    }

    public async Task<ProductoDTO> ObtenerProductoConPlanes(string modelo)
    {
        return await _repository.ObtenerProductoConPlanes(modelo);
    }

    public async Task<decimal> ObtenerMontoCuotaConEntregaMayor(CalculoCuotaRequest request)
    {
        //primeramente obtenemos el monto del precio base
        var precioBase = await _repository.ObtenerPrecioBase(request.Modelo);

        //realizamos el calculo
        // Precio base menos la entrega inicial
        decimal resultado = precioBase - request.EntregaInicial;

        // Cantidad de cuotas multiplicada por el interés
        var interesConfig = _config.GetValue<decimal>("InteresCalculoCuota");
        decimal valorInteresTotal = request.CantidadCuotas * (interesConfig / 100);

        // Resultado multiplicado por el interés total
        decimal montoTotalInteres = resultado * valorInteresTotal;

        // Monto total (resultado más el interés)
        decimal montoTotal = resultado + montoTotalInteres;

        // Cálculo de la cuota
        decimal montoCuota = montoTotal / request.CantidadCuotas;

        // Retornamos el monto de la cuota formateado
        return Math.Round(montoCuota, 0);
    }
}
