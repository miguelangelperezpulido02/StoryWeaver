using Microsoft.EntityFrameworkCore;

namespace Scriptorum.Modelos;

public class WriterContext : DbContext
{
    public DbSet<Proyecto> Proyectos => Set<Proyecto>();
    public DbSet<Personaje> Personajes => Set<Personaje>();
    public DbSet<Relacion> Relaciones => Set<Relacion>();

    private readonly string _dbPath;

    public WriterContext()
    {
        // BD local junto al ejecutable: escritor.db
        var carpeta = AppDomain.CurrentDomain.BaseDirectory;
        _dbPath = System.IO.Path.Combine(carpeta, "escritor.db");
    }

    // Permite inyectar ruta en tests si hiciera falta.
    public WriterContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Proyecto>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Nombre).IsRequired().HasMaxLength(100);
            e.Property(p => p.Descripcion).HasMaxLength(500);
        });

        modelBuilder.Entity<Personaje>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Nombre).IsRequired().HasMaxLength(100);

            // Cada personaje pertenece a un único proyecto.
            e.HasOne<Proyecto>()
                .WithMany()
                .HasForeignKey(p => p.ProyectoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Relacion>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.DescripcionGeneral).HasMaxLength(300);
            e.Property(r => r.DescripcionOrigenADestino).HasMaxLength(500);
            e.Property(r => r.DescripcionDestinoAOrigen).HasMaxLength(500);
            e.Property(r => r.Color).IsRequired().HasMaxLength(20);

            // Cada relación pertenece a un único proyecto.
            e.HasOne<Proyecto>()
                .WithMany()
                .HasForeignKey(r => r.ProyectoId)
                .OnDelete(DeleteBehavior.Restrict);

            // Evita borrados en cascada dobles en SQLite.
            e.HasOne(r => r.PersonajeOrigen)
                .WithMany()
                .HasForeignKey(r => r.PersonajeOrigenId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(r => r.PersonajeDestino)
                .WithMany()
                .HasForeignKey(r => r.PersonajeDestinoId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    // Las versiones anteriores crearon escritor.db sin Color, sin Proyectos
    // y sin ProyectoId. EnsureCreated() no migra, asi que se añade a mano.
    public void AsegurarEsquema()
    {
        Database.EnsureCreated();
        using var conn = Database.GetDbConnection();
        conn.Open();

        if (!ExisteColumna(conn, "Relaciones", "Color"))
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE Relaciones ADD COLUMN Color TEXT NOT NULL DEFAULT '{Utils.ColoresRelacion.Azul}';";
            alter.ExecuteNonQuery();
        }

        if (!ExisteTabla(conn, "Proyectos"))
        {
            using var create = conn.CreateCommand();
            create.CommandText = "CREATE TABLE \"Proyectos\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Proyectos\" PRIMARY KEY AUTOINCREMENT, \"Nombre\" TEXT NOT NULL, \"Descripcion\" TEXT NOT NULL, \"FechaCreacion\" TEXT NOT NULL);";
            create.ExecuteNonQuery();
        }

        if (!ExisteColumna(conn, "Personajes", "ProyectoId"))
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE Personajes ADD COLUMN ProyectoId INTEGER NOT NULL DEFAULT 0;";
            alter.ExecuteNonQuery();
        }

        if (!ExisteColumna(conn, "Relaciones", "ProyectoId"))
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE Relaciones ADD COLUMN ProyectoId INTEGER NOT NULL DEFAULT 0;";
            alter.ExecuteNonQuery();
        }

        // Los datos anteriores (ProyectoId = 0) pasan al proyecto inicial.
        long idDefecto = ObtenerOCrearProyectoInicial(conn);
        ReasignarHuerfanos(conn, "Personajes", idDefecto);
        ReasignarHuerfanos(conn, "Relaciones", idDefecto);
    }

    // Devuelve el Id del primer proyecto, creándolo si no existe ninguno.
    private static long ObtenerOCrearProyectoInicial(System.Data.Common.DbConnection conn)
    {
        using var sel = conn.CreateCommand();
        sel.CommandText = "SELECT MIN(Id) FROM Proyectos;";
        object? primero = sel.ExecuteScalar();
        if (primero != null && primero != DBNull.Value)
            return Convert.ToInt64(primero);

        using var ins = conn.CreateCommand();
        ins.CommandText = "INSERT INTO Proyectos (Nombre, Descripcion, FechaCreacion) VALUES ('Mi historia', '', @fecha);";
        var p = ins.CreateParameter();
        p.ParameterName = "@fecha";
        p.Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        ins.Parameters.Add(p);
        ins.ExecuteNonQuery();

        using var id = conn.CreateCommand();
        id.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt64(id.ExecuteScalar());
    }

    private static void ReasignarHuerfanos(System.Data.Common.DbConnection conn, string tabla, long idProyecto)
    {
        using var upd = conn.CreateCommand();
        upd.CommandText = $"UPDATE \"{tabla}\" SET ProyectoId = @id WHERE ProyectoId = 0;";
        var p = upd.CreateParameter();
        p.ParameterName = "@id";
        p.Value = idProyecto;
        upd.Parameters.Add(p);
        upd.ExecuteNonQuery();
    }

    private static bool ExisteTabla(System.Data.Common.DbConnection conn, string tabla)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @nombre;";
        var p = cmd.CreateParameter();
        p.ParameterName = "@nombre";
        p.Value = tabla;
        cmd.Parameters.Add(p);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static bool ExisteColumna(System.Data.Common.DbConnection conn, string tabla, string columna)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tabla}\");";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(1) == columna)
                return true;
        }
        return false;
    }
}
