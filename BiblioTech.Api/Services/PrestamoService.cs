using BiblioTech.Api.Constants;
using BiblioTech.Api.Data;
using BiblioTech.Api.DTOs.Prestamos;
using BiblioTech.Api.Exceptions;
using BiblioTech.Api.Models;
using BiblioTech.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BiblioTech.Api.Services;


public class PrestamoService(BibliotecaContext db) : IPrestamoService
{
    public async Task<List<PrestamoDto>> ListarAsync(string? estado)
    {
        var ahora = DateTime.Now;
        var consulta = db.Prestamos.AsQueryable();

        consulta = estado switch
        {
            EstadosPrestamo.Activo => consulta.Where(p => p.Estado == EstadosPrestamo.Activo && p.FechaDevolucionEsperada >= ahora),
            EstadosPrestamo.Vencido => consulta.Where(p => p.Estado == EstadosPrestamo.Activo && p.FechaDevolucionEsperada < ahora),
            EstadosPrestamo.Devuelto => consulta.Where(p => p.Estado == EstadosPrestamo.Devuelto),
            _ => consulta
        };

        return await consulta
            .OrderByDescending(p => p.FechaPrestamo)
            .Select(ProyectarDto(ahora))
            .ToListAsync();
    }

    public async Task<PrestamoDto> ObtenerAsync(int id)
    {
        return await db.Prestamos
            .Where(p => p.Id == id)
            .Select(ProyectarDto(DateTime.Now))
            .FirstOrDefaultAsync()
            ?? throw new NoEncontradoException($"El préstamo {id} no existe.");
    }

    public async Task<PrestamoDto> CrearAsync(PrestamoCrearDto dto)
    {
        var ahora = DateTime.Now;

        var socio = await db.Socios
            .Where(s => s.Id == dto.SocioId)
            .Select(s => new { s.Nombre, s.Activo })
            .FirstOrDefaultAsync()
            ?? throw new NoEncontradoException($"El socio {dto.SocioId} no existe.");

        if (!socio.Activo)
            throw new ReglaNegocioException($"El socio {socio.Nombre} está inactivo y no puede pedir préstamos.");   

        var fechasActivos = await db.Prestamos
            .Where(p => p.SocioId == dto.SocioId && p.Estado == EstadosPrestamo.Activo)
            .Select(p => p.FechaDevolucionEsperada)
            .ToListAsync();

        if (fechasActivos.Any(f => f < ahora))
            throw new ReglaNegocioException($"{socio.Nombre} tiene préstamos vencidos y no puede pedir más.");   

        if (fechasActivos.Count >= ReglasPrestamo.MaximoPrestamosActivos)
            throw new ReglaNegocioException(
                $"{socio.Nombre} ya tiene {ReglasPrestamo.MaximoPrestamosActivos} préstamos activos (máximo permitido).");   // R2

        var libro = await db.Libros
            .Where(l => l.Id == dto.LibroId)
            .Select(l => new { l.Titulo, l.EjemplaresDisponibles })
            .FirstOrDefaultAsync()
            ?? throw new NoEncontradoException($"El libro {dto.LibroId} no existe.");

        if (libro.EjemplaresDisponibles <= 0)
            throw new ReglaNegocioException($"No hay ejemplares disponibles de \"{libro.Titulo}\".");   

        await using var transaccion = await db.Database.BeginTransactionAsync();

        var prestamo = new Prestamo
        {
            LibroId = dto.LibroId,
            SocioId = dto.SocioId,
            FechaPrestamo = ahora,
            FechaDevolucionEsperada = ahora.AddDays(ReglasPrestamo.DiasPrestamo),   
            Estado = EstadosPrestamo.Activo
        };
        db.Prestamos.Add(prestamo);
        await db.SaveChangesAsync();                                            
        var filas = await db.Libros
            .Where(l => l.Id == dto.LibroId && l.EjemplaresDisponibles > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.EjemplaresDisponibles, l => l.EjemplaresDisponibles - 1));

        if (filas == 0)
            
            throw new ReglaNegocioException($"No hay ejemplares disponibles de \"{libro.Titulo}\".");   

        await transaccion.CommitAsync();                                        

        return await ObtenerAsync(prestamo.Id);
    }

    public async Task DevolverAsync(int id)
    {
        var prestamo = await db.Prestamos.FindAsync(id)
            ?? throw new NoEncontradoException($"El préstamo {id} no existe.");

        if (prestamo.Estado == EstadosPrestamo.Devuelto)
            throw new ReglaNegocioException("Este préstamo ya fue devuelto.");

        await using var transaccion = await db.Database.BeginTransactionAsync();

        prestamo.Estado = EstadosPrestamo.Devuelto;
        prestamo.FechaDevolucionReal = DateTime.Now;
        await db.SaveChangesAsync();                                            

        await db.Libros
            .Where(l => l.Id == prestamo.LibroId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.EjemplaresDisponibles, l => l.EjemplaresDisponibles + 1));   // R5

        await transaccion.CommitAsync();
    }

    public async Task<List<PrestamoVencidoDto>> ListarVencidosAsync()
    {
        return await db.VwPrestamosVencidos
            .OrderByDescending(v => v.DiasRetraso)
            .Select(v => new PrestamoVencidoDto
            {
                PrestamoId = v.PrestamoId,
                Titulo = v.Titulo,
                Socio = v.Socio,
                Email = v.Email,
                FechaPrestamo = v.FechaPrestamo,
                FechaDevolucionEsperada = v.FechaDevolucionEsperada,
                DiasRetraso = v.DiasRetraso ?? 0
            })
            .ToListAsync();
    }

    private static System.Linq.Expressions.Expression<Func<Prestamo, PrestamoDto>> ProyectarDto(DateTime ahora) => p => new PrestamoDto
    {
        Id = p.Id,
        LibroId = p.LibroId,
        Titulo = p.Libro.Titulo,
        SocioId = p.SocioId,
        Socio = p.Socio.Nombre,
        FechaPrestamo = p.FechaPrestamo,
        FechaDevolucionEsperada = p.FechaDevolucionEsperada,
        FechaDevolucionReal = p.FechaDevolucionReal,
        
        Estado = p.Estado == EstadosPrestamo.Devuelto
            ? EstadosPrestamo.Devuelto
            : p.FechaDevolucionEsperada < ahora ? EstadosPrestamo.Vencido : EstadosPrestamo.Activo
    };
}
