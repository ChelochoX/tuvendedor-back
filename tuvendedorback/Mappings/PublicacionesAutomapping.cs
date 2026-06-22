using AutoMapper;
using tuvendedorback.DTOs;

namespace tuvendedorback.Mappings;

public class PublicacionesAutomapping : Profile
{
    public PublicacionesAutomapping()
    {
        CreateMap<Publicacion, ProductoDto>()
            .ForMember(dest => dest.Nombre, opt => opt.MapFrom(src => src.Titulo))

            .ForMember(dest => dest.Moneda, opt => opt.MapFrom(src =>
                string.IsNullOrWhiteSpace(src.Moneda)
                    ? "PYG"
                    : src.Moneda.Trim().ToUpper()
            ))

            .ForMember(dest => dest.PermiteDelivery, opt => opt.MapFrom(src => src.PermiteDelivery))

            .ForMember(dest => dest.Imagen,
                opt => opt.MapFrom(src =>
                    src.Imagenes != null && src.Imagenes.Any()
                        ? src.Imagenes.First()
                        : null))

            .ForMember(dest => dest.Imagenes, opt => opt.MapFrom(src => src.Imagenes))

            .ForMember(dest => dest.Miniatura,
                opt => opt.MapFrom(src =>
                    src.Imagenes != null && src.Imagenes.Any()
                        ? src.Imagenes.First()
                        : null))

            .ForMember(dest => dest.Vendedor, opt => opt.MapFrom(src =>
                new VendedorDto
                {
                    Nombre = string.IsNullOrEmpty(src.VendedorNombre)
                        ? "Sin nombre"
                        : src.VendedorNombre,
                    Telefono = src.VendedorTelefono
                }
            ))

            .ForMember(dest => dest.PlanCredito, opt => opt.MapFrom(src =>
                src.PlanCredito != null && src.PlanCredito.Any()
                    ? new PlanCreditoDto
                    {
                        Opciones = src.PlanCredito.Select(pc => new PlanOpcionDto
                        {
                            Cuotas = pc.Cuotas,
                            ValorCuota = pc.ValorCuota
                        }).ToList()
                    }
                    : null
            ))

            .ForMember(dest => dest.EsDestacada, opt => opt.MapFrom(src => src.EsDestacada))
            .ForMember(dest => dest.FechaFinDestacado, opt => opt.MapFrom(src => src.FechaFinDestacado))

            .ForMember(dest => dest.EsTemporada, opt => opt.MapFrom(src => src.EsTemporada))
            .ForMember(dest => dest.FechaFinTemporada, opt => opt.MapFrom(src => src.FechaFinTemporada))
            .ForMember(dest => dest.BadgeTexto, opt => opt.MapFrom(src => src.BadgeTexto))
            .ForMember(dest => dest.BadgeColor, opt => opt.MapFrom(src => src.BadgeColor));
    }
}