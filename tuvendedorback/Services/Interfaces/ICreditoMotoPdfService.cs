using tuvendedorback.DTOs;

namespace tuvendedorback.Services.Interfaces;

public interface ICreditoMotoPdfService
{
    Task<CreditoMotoArchivoDto> GenerarPdf(
        int idSolicitudCredito);
}
