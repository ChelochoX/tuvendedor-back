using AutoMapper;
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

    public ClientesService(
        IServiceProvider serviceProvider,
        IImageStorageService imageStorage,
        ILogger<ClientesService> logger,
        IMapper mapper,
        IClientesRepository repository)
    {
        _serviceProvider = serviceProvider;
        _imageStorage = imageStorage;
        _logger = logger;
        _mapper = mapper;
        _repository = repository;
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
}
