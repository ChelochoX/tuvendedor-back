using AutoMapper;
using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Mappings;

public class ClientesAutomapping : Profile
{
    public ClientesAutomapping()
    {
        // 🔹 InteresadoRequest → InteresadoDto
        // (Se usa cuando registrás un nuevo interesado desde el controlador)
        CreateMap<InteresadoRequest, InteresadoDto>()
            .ForMember(dest => dest.Id, opt => opt.Ignore()) // generado por la BD
            .ForMember(dest => dest.FechaRegistro, opt => opt.Ignore()) // manejado por la BD
            .ForMember(dest => dest.Estado, opt => opt.MapFrom(src => "Pendiente"))
            .ForMember(dest => dest.ArchivoUrl, opt => opt.Ignore()) // se completa tras subir a Cloudinary
            .ForMember(dest => dest.UsuarioResponsable, opt => opt.Ignore()); // se setea en el servicio

        // 🔹 SeguimientoRequest → SeguimientoDto
        // (Se usa cuando se agrega un nuevo seguimiento)
        CreateMap<SeguimientoRequest, SeguimientoDto>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.Fecha, opt => opt.Ignore()) // generado automáticamente por la BD
            .ForMember(dest => dest.Usuario, opt => opt.Ignore()); // se setea en el servicio
    }
}
