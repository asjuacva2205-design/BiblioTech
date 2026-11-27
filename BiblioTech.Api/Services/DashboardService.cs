using BiblioTech.Api.Data;
using BiblioTech.Api.DTOs.Dashboard;
using BiblioTech.Api.Services.Interfaces;

namespace BiblioTech.Api.Services;

public class DashboardService(BibliotecaContext db) : IDashboardService
{
    public Task<DashboardDto> ObtenerResumenAsync() => db.ObtenerResumenDashboardAsync();
}
