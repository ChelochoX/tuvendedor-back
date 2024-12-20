﻿using tuvendedorback.DTOs;
using tuvendedorback.Models;
using tuvendedorback.Request;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Services.Interfaces;

public interface IMotoService
{
    Task<List<ModeloMotosporCategoria>> ObtenerModelosPorDefaultAsync();
    Task<ProductoDTO> ObtenerProductoConPlanes(string modelo);
    Task<decimal> ObtenerMontoCuotaConEntregaMayor(CalculoCuotaRequest request);
    Task<int> GuardarSolicitudCredito(SolicitudCredito solicitud);
    Task<byte[]> GenerarPdfSolicitud(SolicitudCredito solicitud, int idSolicitud);
    Task<List<ModeloMotosporCategoria>> ListarProductosConPlanesPromo();
    Task<ProductoDTOPromo> ObtenerProductoConPlanesPromo(string modelo);
    Task<List<ImagenHomeCarrusel>> ObtenerImagenesDesdeHomeCarrusel();
    Task<List<string>> ObtenerImagenesPorModelo(string nombreModelo);
    Task RegistrarVisitaAsync(string page);
    Task<List<string>> GuardarDocumentosAdjuntos(List<IFormFile> archivos, string cedulaCliente);
    Task<Datos<IEnumerable<SolicitudesdeCreditoDTO>>> ObtenerSolicitudesCredito(SolicitudCreditoRequest request);
    Task<CreditoSolicitudDetalleDto> ObtenerDetalleCreditoSolicitudAsync(int id);
    Task<bool> ActualizarSolicitudCredito(int idSolicitud, SolicitudCredito solicitud);
    Task<IEnumerable<VisitaPagina>> ObtenerEstadisticasDeAcceso();
    Task<IEnumerable<CreditoEstadisticasDto>> ObtenerEstadisticasCreditos();
    Task<List<ModeloMotosporCategoria>> ObtenerModelosPorCoincidenciaAsync(string expresionBusqueda);
}
