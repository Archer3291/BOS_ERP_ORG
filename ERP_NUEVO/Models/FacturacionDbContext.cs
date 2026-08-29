// Models/FacturacionDbContext.cs
using Microsoft.EntityFrameworkCore;

namespace BOS_ERP.Models
{
    public class FacturacionDbContext : DbContext
    {      

        public FacturacionDbContext(DbContextOptions<FacturacionDbContext> options)
            : base(options)
        {
        }

        public DbSet<Cliente> Clientes { get; set; }
        public DbSet<Usuario> Usuarios { get; set; }
        public DbSet<Rol> Roles { get; set; }
        public DbSet<UserSession> UserSessions { get; set; }
        public DbSet<Licencia> Licencias { get; set; }
        public DbSet<Empresa> Empresas { get; set; }
        public DbSet<tkt_usuario_rol> tkt_usuario_rol { get; set; }
        public DbSet<Permisos> Permisos { get; set; }
        public DbSet<Sucursales> SucursalId { get; set; }
        public DbSet<Area> AreaId { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {

            modelBuilder.HasDefaultSchema("srs");

            modelBuilder.Entity<UserSession>(entity =>
            {
                entity.ToTable("usersessions", "srs");

                entity.Property(e => e.ExpiryTime)
                    .HasColumnName("expirytime")
                    .HasColumnType("timestamp without time zone");

                entity.Property(e => e.LastActivity)
                    .HasColumnName("lastactivity")
                    .HasColumnType("timestamp without time zone");

                entity.Property(e => e.CreatedAt)
                    .HasColumnName("createdat")
                    .HasColumnType("timestamp without time zone");
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}