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

    public ClientesService(IServiceProvider serviceProvider, IImageStorageService imageStorage, ILogger<ClientesService> logger, IMapper mapper, IClientesRepository repository)
    {
        _serviceProvider = serviceProvider;
        _imageStorage = imageStorage;
        _logger = logger;
        _mapper = mapper;
        _repository = repository;
    }

    public async Task<int> RegistrarInteresado(InteresadoRequest request, int idUsuario)
    {
        // 🔹 Validación con FluentValidation
        await ValidationHelper.ValidarAsync(request, _serviceProvider);

        var dto = _mapper.Map<InteresadoDto>(request);
        dto.FechaRegistro = DateTime.Now;
        dto.Estado = "Pendiente";
        dto.UsuarioResponsable = idUsuario.ToString();

        // 📎 Subir archivo de conversación si lo envió
        if (request.ArchivoConversacion != null)
        {
            var uploadResult = await _imageStorage.SubirArchivo(request.ArchivoConversacion, "interesados");
            dto.ArchivoUrl = uploadResult.MainUrl;
        }

        var id = await _repository.InsertarInteresado(dto);
        _logger.LogInformation("Interesado {Nombre} creado por usuario {IdUsuario}", dto.Nombre, idUsuario);
        return id;
    }

    public async Task<int> AgregarSeguimiento(SeguimientoRequest request, int idUsuario)
    {
        await ValidationHelper.ValidarAsync(request, _serviceProvider);

        var dto = _mapper.Map<SeguimientoDto>(request);
        dto.Usuario = idUsuario.ToString();
        dto.Fecha = DateTime.Now;

        var id = await _repository.InsertarSeguimiento(dto);
        _logger.LogInformation("Seguimiento agregado por {IdUsuario} al interesado {IdInteresado}", idUsuario, dto.IdInteresado);
        return id;
    }

    public async Task<(List<InteresadoDto> Items, int Total)> ObtenerInteresados(FiltroInteresadosRequest filtro)
    {
        var (items, total) = await _repository.ObtenerInteresados(filtro);
        _logger.LogInformation("Se obtuvieron {Count} interesados (total: {Total})", items.Count, total);
        return (items, total);
    }

    public async Task<List<SeguimientoDto>> ObtenerSeguimientosPorInteresado(int idInteresado)
    {
        return await _repository.ObtenerSeguimientosPorInteresado(idInteresado);
    }

    public async Task ActualizarInteresado(int id, InteresadoRequest request, int idUsuario)
    {
        await ValidationHelper.ValidarAsync(request, _serviceProvider);

        // Obtener el registro actual
        var existente = await _repository.ObtenerInteresadoPorId(id);
        if (existente == null)
            throw new RepositoryException($"No se encontró el interesado con Id {id}");

        string? nuevaUrl = existente.ArchivoUrl;

        // 📁 Si hay nuevo archivo, reemplazar
        if (request.ArchivoConversacion != null)
        {
            // Eliminar el anterior si existía
            if (!string.IsNullOrEmpty(existente.ArchivoUrl))
            {
                try
                {
                    await _imageStorage.EliminarArchivo(existente.ArchivoUrl);
                    _logger.LogInformation("Archivo anterior eliminado correctamente para Id {Id}", id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo eliminar el archivo anterior de Id {Id}", id);
                }
            }

            // Subir el nuevo archivo
            var uploadResult = await _imageStorage.SubirArchivo(request.ArchivoConversacion, "interesados");
            nuevaUrl = uploadResult.MainUrl;
        }

        // Mapear actualización
        var dto = _mapper.Map<InteresadoDto>(request);
        dto.Id = id;
        dto.ArchivoUrl = nuevaUrl;
        dto.UsuarioResponsable = idUsuario.ToString();

        await _repository.ActualizarInteresado(dto);

        _logger.LogInformation("Interesado {Id} actualizado por usuario {Usuario}", id, idUsuario);
    }


}
