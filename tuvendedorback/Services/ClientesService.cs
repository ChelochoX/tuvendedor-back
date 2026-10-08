using AutoMapper;
using System.Text.Json;
using tuvendedorback.Common;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class ClientesService : IClientesService
{
    private readonly IClientesRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<ClientesService> _logger;
    private readonly IImageStorageService _imageStorage;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public ClientesService(
        IServiceProvider serviceProvider,
        IImageStorageService imageStorage,
        ILogger<ClientesService> logger,
        IMapper mapper,
        IClientesRepository repository,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _imageStorage = imageStorage;
        _logger = logger;
        _mapper = mapper;
        _repository = repository;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }


    public async Task<int> RegistrarInteresado(
        InteresadoRequest request,
        int idUsuario)
    {
        await ValidationHelper.ValidarAsync(
            request,
            _serviceProvider);

        var dto =
            _mapper.Map<InteresadoDto>(
                request);

        dto.FechaRegistro =
            DateTime.Now;

        dto.Estado =
            "Activo";

        dto.UsuarioResponsable =
            idUsuario.ToString();

        dto.Origen =
            "MANUAL";

        dto.EstadoConsulta =
            "REGISTRADO";

        dto.RequiereSeguimiento =
            true;

        dto.MotivoSeguimiento =
            "Seguimiento comercial manual.";

        dto.FechaUltimaInteraccion =
            DateTime.Now;

        dto.CantidadInteracciones =
            0;

        if (request.ArchivoConversacion != null)
        {
            var uploadResult =
                await _imageStorage.SubirArchivo(
                    request.ArchivoConversacion,
                    "interesados");

            dto.ArchivoUrl =
                uploadResult.MainUrl;
        }

        var id =
            await _repository.InsertarInteresado(
                dto);

        _logger.LogInformation(
            "Interesado {Nombre} creado por usuario {IdUsuario}",
            dto.Nombre,
            idUsuario);

        return id;
    }


    public async Task<int> RegistrarInteraccionWhatsApp(
        InteresadoWhatsAppEventoRequest request)
    {
        if (request.IdConversacion <= 0)
        {
            throw new ReglasdeNegocioException(
                "No se pudo identificar la conversación de WhatsApp.");
        }

        return await _repository
            .RegistrarInteraccionWhatsApp(
                request);
    }



    public async Task<SincronizacionWhatsAppResultadoDto>
        SincronizarWhatsAppDia(
            DateTime? fecha,
            int idUsuario)
    {
        var dia =
            (fecha ?? DateTime.Today).Date;

        var conversaciones =
            await _repository
                .ObtenerConversacionesWhatsAppDia(
                    dia);

        var resultado =
            new SincronizacionWhatsAppResultadoDto
            {
                Fecha = dia,
                ChatsEncontrados =
                    conversaciones.Count
            };

        if (conversaciones.Count == 0)
        {
            return resultado;
        }

        var entorno =
            Environment.GetEnvironmentVariable(
                "ASPNETCORE_ENVIRONMENT");

        var urlPorDefecto =
            string.Equals(
                entorno,
                "Development",
                StringComparison.OrdinalIgnoreCase)
                ? "http://localhost:3100"
                : "http://tuvendedor_wa:3100";

        var baseUrl =
            (
                _configuration[
                    "WhatsAppBridge:BaseUrl"]
                ??
                Environment.GetEnvironmentVariable(
                    "WHATSAPP_BRIDGE_URL")
                ??
                urlPorDefecto
            )
            .TrimEnd('/');

        var internalKey =
            _configuration[
                "IA:InternalKey"];

        if (string.IsNullOrWhiteSpace(
            internalKey))
        {
            throw new ReglasdeNegocioException(
                "No está configurada la clave interna para sincronizar WhatsApp.");
        }

        var identificadores =
            conversaciones
                .Select(
                    x =>
                        x.IdentificadorExterno)
                .Where(
                    x =>
                        !string.IsNullOrWhiteSpace(
                            x))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        var client =
            _httpClientFactory.CreateClient();

        client.Timeout =
            TimeSpan.FromSeconds(120);

        using var httpRequest =
            new HttpRequestMessage(
                HttpMethod.Post,
                $"{baseUrl}/crm/resolver-identidades");

        httpRequest.Headers.TryAddWithoutValidation(
            "X-TuVendedor-Internal-Key",
            internalKey);

        httpRequest.Content =
            JsonContent.Create(
                new
                {
                    identificadores
                });

        using var response =
            await client.SendAsync(
                httpRequest);

        var json =
            await response.Content
                .ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Bridge WhatsApp devolvió {StatusCode}: {Body}",
                (int)response.StatusCode,
                json);

            throw new ReglasdeNegocioException(
                "No se pudo consultar la sesión de WhatsApp. Revisá que el bridge esté conectado.");
        }

        var bridge =
            JsonSerializer.Deserialize<
                WhatsAppResolverBridgeResponse>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive =
                        true
                });

        if (
            bridge is null
            ||
            !bridge.Success
        )
        {
            throw new ReglasdeNegocioException(
                bridge?.Message
                ??
                "WhatsApp no devolvió una respuesta válida.");
        }

        var identidades =
            bridge.Data
                .Where(
                    x =>
                        !string.IsNullOrWhiteSpace(
                            x.IdentificadorExterno))
                .GroupBy(
                    x =>
                        x.IdentificadorExterno,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => x.Last(),
                    StringComparer.OrdinalIgnoreCase);

        foreach (
            var conversacion
            in conversaciones)
        {
            try
            {
                identidades.TryGetValue(
                    conversacion
                        .IdentificadorExterno,
                    out var identidad);

                var item =
                    await _repository
                        .SincronizarContactoWhatsApp(
                            new WhatsAppContactoSincronizacionRequest
                            {
                                IdentificadorExterno =
                                    conversacion
                                        .IdentificadorExterno,

                                NumeroWhatsapp =
                                    identidad
                                        ?.NumeroWhatsapp,

                                NombreContacto =
                                    identidad
                                        ?.NombreContacto,

                                UltimoMensajeCliente =
                                    conversacion
                                        .UltimoMensajeCliente,

                                UltimaRespuesta =
                                    conversacion
                                        .UltimaRespuesta,

                                FechaUltimoMensajeCliente =
                                    conversacion
                                        .FechaUltimoMensajeCliente,

                                FechaUltimaRespuesta =
                                    conversacion
                                        .FechaUltimaRespuesta,

                                FechaUltimaInteraccion =
                                    conversacion
                                        .FechaUltimaInteraccion,

                                CantidadMensajesDia =
                                    conversacion
                                        .CantidadMensajesDia
                            });

                resultado.Procesados++;

                if (item.EsNuevo)
                {
                    resultado.Nuevos++;
                }
                else
                {
                    resultado.Actualizados++;
                }

                if (item.TieneTelefonoReal)
                {
                    resultado.ConTelefonoReal++;
                }
                else
                {
                    resultado.SinTelefonoReal++;
                }
            }
            catch (Exception ex)
            {
                resultado.Errores++;

                _logger.LogWarning(
                    ex,
                    "No se pudo sincronizar chat WhatsApp {Identificador}",
                    conversacion
                        .IdentificadorExterno);
            }
        }

        _logger.LogInformation(
            "Sincronización WhatsApp {Fecha}: {Procesados} procesados, {Nuevos} nuevos, {Actualizados} actualizados, {ConTelefono} con teléfono, {SinTelefono} sin teléfono, {Errores} errores. Usuario {IdUsuario}",
            dia,
            resultado.Procesados,
            resultado.Nuevos,
            resultado.Actualizados,
            resultado.ConTelefonoReal,
            resultado.SinTelefonoReal,
            resultado.Errores,
            idUsuario);

        return resultado;
    }


    public async Task<int> AgregarSeguimiento(
        SeguimientoRequest request,
        int idUsuario)
    {
        await ValidationHelper.ValidarAsync(
            request,
            _serviceProvider);

        var dto =
            _mapper.Map<SeguimientoDto>(
                request);

        dto.Usuario =
            idUsuario.ToString();

        dto.Fecha =
            DateTime.Now;

        var id =
            await _repository.InsertarSeguimiento(
                dto);

        _logger.LogInformation(
            "Seguimiento agregado por {IdUsuario} al interesado {IdInteresado}",
            idUsuario,
            dto.IdInteresado);

        return id;
    }


    public async Task<(List<InteresadoDto> Items, int Total)>
        ObtenerInteresados(
            FiltroInteresadosRequest filtro)
    {
        if (filtro.NumeroPagina <= 0)
        {
            filtro.NumeroPagina = 1;
        }

        if (filtro.RegistrosPorPagina <= 0)
        {
            filtro.RegistrosPorPagina = 10;
        }

        var (items, total) =
            await _repository.ObtenerInteresados(
                filtro);

        return (
            items,
            total
        );
    }


    public async Task<List<SeguimientoDto>>
        ObtenerSeguimientosPorInteresado(
            int idInteresado)
    {
        return await _repository
            .ObtenerSeguimientosPorInteresado(
                idInteresado);
    }


    public async Task<InteresadoDetalleDto>
        ObtenerDetalleInteresado(
            int idInteresado)
    {
        if (idInteresado <= 0)
        {
            throw new ReglasdeNegocioException(
                "El identificador del interesado no es válido.");
        }

        var detalle =
            await _repository.ObtenerDetalleInteresado(
                idInteresado);

        if (detalle is null)
        {
            throw new ReglasdeNegocioException(
                $"No se encontró el interesado con Id {idInteresado}");
        }

        return detalle;
    }


    public Task<InteresadosResumenDto>
        ObtenerResumenInteresados(
            DateTime? fecha)
    {
        return _repository
            .ObtenerResumenInteresados(
                fecha?.Date);
    }


    public async Task ActualizarSeguimientoInteresado(
        int idInteresado,
        ActualizarSeguimientoInteresadoRequest request,
        int idUsuario)
    {
        if (idInteresado <= 0)
        {
            throw new ReglasdeNegocioException(
                "El identificador del interesado no es válido.");
        }

        if (request is null)
        {
            throw new ReglasdeNegocioException(
                "Debe indicar los datos del seguimiento.");
        }

        await _repository
            .ActualizarSeguimientoInteresado(
                idInteresado,
                request);

        if (!string.IsNullOrWhiteSpace(
            request.Comentario))
        {
            await _repository.InsertarSeguimiento(
                new SeguimientoDto
                {
                    IdInteresado =
                        idInteresado,

                    Fecha =
                        DateTime.Now,

                    Comentario =
                        request.Comentario.Trim(),

                    Usuario =
                        idUsuario.ToString()
                });
        }
    }


    public async Task ActualizarInteresado(
        int id,
        InteresadoRequest request,
        int idUsuario)
    {
        await ValidationHelper.ValidarAsync(
            request,
            _serviceProvider);

        var actual =
            await _repository.ObtenerInteresadoPorId(
                id);

        if (actual is null)
        {
            throw new ReglasdeNegocioException(
                $"No se encontró el interesado con Id {id}");
        }

        var estadoAnterior =
            actual.Estado
            ??
            "Activo";

        var dto =
            _mapper.Map<InteresadoDto>(
                request);

        dto.Id =
            id;

        if (request.ArchivoConversacion != null)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(
                    actual.ArchivoUrl))
                {
                    await _imageStorage.EliminarArchivo(
                        actual.ArchivoUrl);
                }

                var upload =
                    await _imageStorage.SubirArchivo(
                        request.ArchivoConversacion,
                        "interesados");

                dto.ArchivoUrl =
                    upload.MainUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error al reemplazar archivo para interesado {Id}",
                    id);

                throw new RepositoryException(
                    "Error al reemplazar archivo",
                    ex);
            }
        }
        else
        {
            dto.ArchivoUrl =
                actual.ArchivoUrl;
        }

        dto.Estado ??=
            actual.Estado
            ??
            "Activo";

        await _repository.ActualizarInteresado(
            dto);

        if (
            estadoAnterior == "Activo"
            &&
            dto.Estado == "Inactivo"
        )
        {
            await _repository.InsertarSeguimiento(
                new SeguimientoDto
                {
                    IdInteresado =
                        id,

                    Fecha =
                        DateTime.Now,

                    Comentario =
                        "Cierre de interesado (estado cambiado a Inactivo).",

                    Usuario =
                        idUsuario.ToString()
                });
        }
    }

    private sealed class WhatsAppResolverBridgeResponse
    {
        public bool Success { get; set; }

        public string? Message { get; set; }

        public int Errores { get; set; }

        public List<WhatsAppResolverBridgeItem>
            Data
        { get; set; } = new();
    }


    private sealed class WhatsAppResolverBridgeItem
    {
        public string IdentificadorExterno { get; set; } =
            string.Empty;

        public string? NumeroWhatsapp { get; set; }

        public string? NombreContacto { get; set; }

        public string? Error { get; set; }
    }

}
