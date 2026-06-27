using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;
using static tuvendedorback.Common.ServiciosPremiumConstants;

namespace tuvendedorback.Services;

public class ServicioPremiumService :
    IServicioPremiumService
{
    private readonly IServicioPremiumRepository
        _repository;

    private readonly ILogger<ServicioPremiumService>
        _logger;

    public ServicioPremiumService(
        IServicioPremiumRepository repository,
        ILogger<ServicioPremiumService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<int> CrearSolicitud(
        CrearSolicitudServicioPremiumRequest request,
        int idUsuario)
    {
        if (idUsuario <= 0)
        {
            throw new UnauthorizedAccessException();
        }

        if (request == null)
        {
            throw new ReglasdeNegocioException(
                "La solicitud es obligatoria.");
        }

        request.TipoServicio =
            NormalizarTipoServicio(
                request.TipoServicio);

        if (
            !TiposServicioPremium
                .PermitidosParaSolicitud
                .Contains(
                    request.TipoServicio))
        {
            throw new ReglasdeNegocioException(
                "El tipo de servicio Premium solicitado no es válido.");
        }

        var idVendedor =
            await _repository
                .ObtenerIdVendedorPorUsuario(
                    idUsuario);

        if (
            !idVendedor
                .HasValue)
        {
            throw new ReglasdeNegocioException(
                "Solo los vendedores pueden solicitar servicios Premium.");
        }

        if (
            RequierePublicacion(
                request.TipoServicio))
        {
            if (
                !request
                    .IdPublicacion
                    .HasValue
                ||
                request
                    .IdPublicacion
                    .Value
                <= 0)
            {
                throw new ReglasdeNegocioException(
                    "Debe seleccionar una publicación.");
            }

            var perteneceAlVendedor =
                await _repository
                    .EsPublicacionDelVendedor(
                        request
                            .IdPublicacion
                            .Value,

                        idVendedor
                            .Value);

            if (
                !perteneceAlVendedor)
            {
                throw new ReglasdeNegocioException(
                    "No puedes solicitar un servicio para una publicación que no te pertenece.");
            }
        }
        else
        {
            request.IdPublicacion =
                null;
        }

        var existeSolicitudPendiente =
            await _repository
                .ExisteSolicitudPendiente(
                    idVendedor
                        .Value,

                    request
                        .TipoServicio,

                    request
                        .IdPublicacion);

        if (
            existeSolicitudPendiente)
        {
            throw new ReglasdeNegocioException(
                "Ya existe una solicitud pendiente para este servicio.");
        }

        var idServicio =
            await _repository
                .CrearSolicitud(
                    idVendedor
                        .Value,

                    request);

        _logger.LogInformation(
            "Solicitud Premium creada correctamente. IdServicio={IdServicio}, IdUsuario={IdUsuario}",
            idServicio,
            idUsuario);

        return idServicio;
    }

    public async Task<
        Datos<List<ServicioPremiumDto>>
    > ObtenerServiciosParaAdministrador(
        FiltrosServiciosPremiumRequest filtros,
        int idUsuarioAdmin)
    {
        await ValidarAdministrador(
            idUsuarioAdmin);

        filtros ??=
            new FiltrosServiciosPremiumRequest();

        filtros.Pagina =
            filtros.Pagina <= 0
                ? 1
                : filtros.Pagina;

        filtros.TamanioPagina =
            filtros.TamanioPagina <= 0
                ? 10
                : Math.Min(
                    filtros.TamanioPagina,
                    100);

        filtros.Estado =
            NormalizarEstadoOpcional(
                filtros.Estado);

        filtros.TipoServicio =
            NormalizarTipoServicioOpcional(
                filtros.TipoServicio);

        filtros.Cliente =
            string.IsNullOrWhiteSpace(
                filtros.Cliente)
                ? null
                : filtros
                    .Cliente
                    .Trim();

        if (
            filtros
                .FechaDesde
                .HasValue
            &&
            filtros
                .FechaHasta
                .HasValue
            &&
            filtros
                .FechaHasta
                .Value
                .Date
            <
            filtros
                .FechaDesde
                .Value
                .Date)
        {
            throw new ReglasdeNegocioException(
                "La fecha final no puede ser anterior a la fecha inicial.");
        }

        await _repository
            .SincronizarVencimientos();

        return await _repository
            .ObtenerServicios(
                filtros);
    }

    public async Task<
        ServicioPremiumDto
    > ObtenerServicioPorIdParaAdministrador(
        int idServicio,
        int idUsuarioAdmin)
    {
        await ValidarAdministrador(
            idUsuarioAdmin);

        await _repository
            .SincronizarVencimientos();

        var servicio =
            await _repository
                .ObtenerServicioPorId(
                    idServicio);

        if (
            servicio ==
            null)
        {
            throw new NoDataFoundException(
                "No se encontró el servicio Premium.");
        }

        return servicio;
    }

    public async Task<
        ResumenServiciosPremiumDto
    > ObtenerResumenParaAdministrador(
        int idUsuarioAdmin)
    {
        await ValidarAdministrador(
            idUsuarioAdmin);

        await _repository
            .SincronizarVencimientos();

        return await _repository
            .ObtenerResumen();
    }

    public async Task ActivarServicio(
        int idServicio,
        ActivarServicioPremiumRequest request,
        int idUsuarioAdmin)
    {
        await ValidarAdministrador(
            idUsuarioAdmin);

        if (request == null)
        {
            throw new ReglasdeNegocioException(
                "Los datos de activación son obligatorios.");
        }

        ValidarDatosComerciales(
            request);

        var servicio =
            await _repository
                .ObtenerServicioPorId(
                    idServicio);

        if (servicio == null)
        {
            throw new NoDataFoundException(
                "No se encontró el servicio Premium.");
        }

        if (servicio.Estado == EstadosServicioPremium.Activo)
        {
            throw new ReglasdeNegocioException(
                "El servicio Premium ya se encuentra activo.");
        }

        if (servicio.Estado == EstadosServicioPremium.Cancelado)
        {
            throw new ReglasdeNegocioException(
                "No se puede activar un servicio cancelado.");
        }

        switch (servicio.TipoServicio)
        {
            case TiposServicioPremium.VitrinaProfesional:

            case TiposServicioPremium.PublicacionDestacada:
                {
                    ResolverFechasDesdeDuracion(
                        request);

                    ValidarFechas(
                        request);

                    break;
                }

            case TiposServicioPremium.PublicacionEspecial:
                {
                    var modo =
                        NormalizarModoActivacionEspecial(
                            request);

                    request.ModoActivacionEspecial =
                        modo;

                    if (modo == "TEMPORADA")
                    {
                        if (!request.IdTemporada.HasValue || request.IdTemporada.Value <= 0)
                        {
                            throw new ReglasdeNegocioException(
                                "Debe seleccionar una temporada comercial.");
                        }

                        request.DuracionDias =
                            null;

                        request.FechaInicio =
                            null;

                        request.FechaFin =
                            null;
                    }
                    else
                    {
                        request.IdTemporada =
                            null;

                        ResolverFechasDesdeDuracion(
                            request);

                        ValidarFechas(
                            request);

                        request.BadgeTexto =
                            string.IsNullOrWhiteSpace(request.BadgeTexto)
                                ? "ESPECIAL"
                                : request.BadgeTexto.Trim();

                        request.BadgeColor =
                            string.IsNullOrWhiteSpace(request.BadgeColor)
                                ? "#A855F7"
                                : request.BadgeColor.Trim();
                    }

                    break;
                }

            default:
                {
                    throw new ReglasdeNegocioException(
                        "El tipo de servicio Premium no está soportado.");
                }
        }

        await _repository
            .ActivarServicio(
                idServicio,
                request,
                idUsuarioAdmin);
    }

    public async Task CancelarServicio(
        int idServicio,
        CancelarServicioPremiumRequest request,
        int idUsuarioAdmin)
    {
        await ValidarAdministrador(
            idUsuarioAdmin);

        var servicio =
            await _repository
                .ObtenerServicioPorId(
                    idServicio);

        if (
            servicio ==
            null)
        {
            throw new NoDataFoundException(
                "No se encontró el servicio Premium.");
        }

        if (
            servicio.Estado ==
            EstadosServicioPremium.Cancelado)
        {
            throw new ReglasdeNegocioException(
                "El servicio Premium ya se encuentra cancelado.");
        }

        await _repository
            .CancelarServicio(
                idServicio,
                request
                    ?.Observacion,
                idUsuarioAdmin);
    }

    private async Task ValidarAdministrador(
        int idUsuario)
    {
        if (
            idUsuario <=
            0)
        {
            throw new UnauthorizedAccessException();
        }

        var esAdministrador =
            await _repository
                .EsAdministrador(
                    idUsuario);

        if (
            !esAdministrador)
        {
            throw new ReglasdeNegocioException(
                "No tienes permisos para administrar servicios Premium.");
        }
    }

    private static string NormalizarTipoServicio(
        string? tipoServicio)
    {
        if (
            string.IsNullOrWhiteSpace(
                tipoServicio))
        {
            throw new ReglasdeNegocioException(
                "El tipo de servicio Premium es obligatorio.");
        }

        return tipoServicio
            .Trim()
            .ToUpperInvariant();
    }

    private static string?
        NormalizarTipoServicioOpcional(
            string? tipoServicio)
    {
        if (
            string.IsNullOrWhiteSpace(
                tipoServicio))
        {
            return null;
        }

        var valor =
            tipoServicio
                .Trim()
                .ToUpperInvariant();

        if (
            !TiposServicioPremium
                .Permitidos
                .Contains(
                    valor))
        {
            throw new ReglasdeNegocioException(
                "El tipo de servicio indicado no es válido.");
        }

        return valor;
    }

    private static string?
        NormalizarEstadoOpcional(
            string? estado)
    {
        if (
            string.IsNullOrWhiteSpace(
                estado))
        {
            return null;
        }

        var valor =
            estado
                .Trim()
                .ToUpperInvariant();

        if (
            !EstadosServicioPremium
                .Permitidos
                .Contains(
                    valor))
        {
            throw new ReglasdeNegocioException(
                "El estado indicado no es válido.");
        }

        return valor;
    }

    private static bool RequierePublicacion(
        string tipoServicio)
    {
        return
            tipoServicio ==
                TiposServicioPremium
                    .PublicacionDestacada
            ||
            tipoServicio ==
                TiposServicioPremium
                    .PublicacionEspecial;
    }

    private static string NormalizarModoActivacionEspecial(
    ActivarServicioPremiumRequest request)
    {
        var modo =
            request.ModoActivacionEspecial
                ?.Trim()
                .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(modo))
        {
            modo =
                request.IdTemporada.HasValue
                &&
                request.IdTemporada.Value > 0
                    ? "TEMPORADA"
                    : "DIAS";
        }

        if (modo != "DIAS" && modo != "TEMPORADA")
        {
            throw new ReglasdeNegocioException(
                "El modo de activación especial debe ser DIAS o TEMPORADA.");
        }

        return modo;
    }
    private static void ResolverFechasDesdeDuracion(
        ActivarServicioPremiumRequest request)
    {
        if (!request.DuracionDias.HasValue)
        {
            return;
        }

        if (
            request.DuracionDias.Value <= 0
            ||
            request.DuracionDias.Value > 365)
        {
            throw new ReglasdeNegocioException(
                "La duración del servicio debe ser mayor a 0 y no puede superar 365 días.");
        }

        var fechaInicio =
            request.FechaInicio
            ?? DateTime.Now;

        request.FechaInicio =
            fechaInicio;

        request.FechaFin =
            fechaInicio.AddDays(
                request.DuracionDias.Value);
    }

    private static void ValidarFechas(
        ActivarServicioPremiumRequest request)
    {
        if (
            !request
                .FechaInicio
                .HasValue
            ||
            !request
                .FechaFin
                .HasValue)
        {
            throw new ReglasdeNegocioException(
                "Debe indicar la fecha de inicio y la fecha de finalización.");
        }

        if (
            request
                .FechaFin
                .Value
            <=
            request
                .FechaInicio
                .Value)
        {
            throw new ReglasdeNegocioException(
                "La fecha final debe ser posterior a la fecha inicial.");
        }
    }

    private static void ValidarDatosComerciales(
        ActivarServicioPremiumRequest request)
    {
        if (
            !request
                .Monto
                .HasValue)
        {
            throw new ReglasdeNegocioException(
                "Debe indicar el monto cobrado. Para una cortesía utilice el valor 0.");
        }

        if (
            request
                .Monto
                .Value
            < 0)
        {
            throw new ReglasdeNegocioException(
                "El monto cobrado no puede ser negativo.");
        }

        if (
            string.IsNullOrWhiteSpace(
                request
                    .MedioPago))
        {
            throw new ReglasdeNegocioException(
                "Debe indicar el medio de pago. Para una cortesía utilice CORTESIA.");
        }

        request.MedioPago =
            request
                .MedioPago
                .Trim()
                .ToUpperInvariant();

        request.ReferenciaPago =
            request
                .ReferenciaPago
                ?.Trim();

        request.Observacion =
            request
                .Observacion
                ?.Trim();

        if (
            request
                .Monto
                .Value
            ==
            0
            &&
            request
                .MedioPago
            !=
            "CORTESIA")
        {
            throw new ReglasdeNegocioException(
                "Cuando el monto es 0, el medio de pago debe ser CORTESIA.");
        }

        if (
            request
                .Monto
                .Value
            ==
            0
            &&
            string.IsNullOrWhiteSpace(
                request
                    .Observacion))
        {
            throw new ReglasdeNegocioException(
                "Debe indicar el motivo de la cortesía en la observación.");
        }
    }
}
