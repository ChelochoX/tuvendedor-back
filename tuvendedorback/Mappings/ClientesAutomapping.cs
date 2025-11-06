using AutoMapper;
using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Mappings;

public class ClientesAutomapping : Profile
{
    public ClientesAutomapping()
    {
        // 🔹 InteresadoRequest → InteresadoDto
        CreateMap<InteresadoRequest, InteresadoDto>()
        .ForMember(dest => dest.Id, opt => opt.Ignore()) // generado por la BD
        .ForMember(dest => dest.FechaRegistro, opt => opt.Ignore()) // manejado por la BD
        .ForMember(dest => dest.Estado, opt => opt.MapFrom(src => src.Estado ?? "Activo")) // por defecto Activo
        .ForMember(dest => dest.ArchivoUrl, opt => opt.Ignore()) // se completa tras subir a Cloudinary
        .ForMember(dest => dest.UsuarioResponsable, opt => opt.Ignore()); // se setea en el servicio

        // 🟡 InteresadoRequest → InteresadoDto (para actualizar existente)
        CreateMap<InteresadoRequest, InteresadoDto>()
        .ForMember(dest => dest.Id, opt => opt.Ignore())
        .ForMember(dest => dest.FechaRegistro, opt => opt.Ignore())
        .ForMember(dest => dest.ArchivoUrl, opt => opt.Ignore())
        .ForMember(dest => dest.UsuarioResponsable, opt => opt.Ignore())
        .ForMember(dest => dest.Estado, opt => opt.MapFrom(src => src.Estado ?? "Activo"))
        .ForAllMembers(opt => opt.Condition((src, dest, srcMember) => srcMember != null)); // no sobrescribir con nulls

        // 🔹 SeguimientoRequest → SeguimientoDto
        // (Se usa cuando se agrega un nuevo seguimiento)
        CreateMap<SeguimientoRequest, SeguimientoDto>()
        .ForMember(dest => dest.Id, opt => opt.Ignore())
        .ForMember(dest => dest.Fecha, opt => opt.Ignore()) // generado automáticamente por la BD
        .ForMember(dest => dest.Usuario, opt => opt.Ignore()); // se setea en el servicio
    }
}
