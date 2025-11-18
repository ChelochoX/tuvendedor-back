using AutoMapper;
using tuvendedorback.DTOs;
using tuvendedorback.Models;

namespace tuvendedorback.Mappings;
public class PublicacionesAutomapping : Profile
{
    public PublicacionesAutomapping()
    {
        CreateMap<Publicacion, ProductoDto>()
            .ForMember(dest => dest.Nombre, opt => opt.MapFrom(src => src.Titulo))
            .ForMember(dest => dest.Imagen, opt => opt.MapFrom(src => src.Imagenes.FirstOrDefault() != null ? src.Imagenes.First().Url : null))
            .ForMember(dest => dest.Imagenes, opt => opt.MapFrom(src => src.Imagenes.Select(i => i.Url)))
            .ForMember(dest => dest.Vendedor, opt => opt.MapFrom(src =>
                new VendedorDto
                {
                    Nombre = string.IsNullOrEmpty(src.VendedorNombre) ? "Sin nombre" : src.VendedorNombre,
                    Avatar = string.IsNullOrEmpty(src.VendedorAvatar)
                        ? $"https://ui-avatars.com/api/?name={Uri.EscapeDataString(src.VendedorNombre ?? "SN")}"
                        : src.VendedorAvatar
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
            .ForMember(dest => dest.FechaFinDestacado, opt => opt.MapFrom(src => src.FechaFinDestacado));
    }
}