using Microsoft.EntityFrameworkCore;

namespace Scriptorum.Modelos;

public class WriterContext : DbContext
{
    public DbSet<Proyecto> Proyectos => Set<Proyecto>();
    public DbSet<Personaje> Personajes => Set<Personaje>();
    public DbSet<Relacion> Relaciones => Set<Relacion>();
    public DbSet<Cronologia> Cronologias => Set<Cronologia>();
    public DbSet<Nota> Notas => Set<Nota>();
    public DbSet<LineaTiempo> LineasTiempo => Set<LineaTiempo>();

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
            e.Property(p => p.Grupo).HasMaxLength(100);

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

        modelBuilder.Entity<Cronologia>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Titulo).IsRequired().HasMaxLength(150);
            e.Property(c => c.Descripcion).HasMaxLength(2000);
            e.Property(c => c.Tipo).IsRequired().HasMaxLength(20);

            // Referencia opcional a una nota asociada a este evento.
            e.Property(c => c.NotaId).HasColumnName("NotaId");

            // Cada evento pertenece a un único proyecto y a una línea de tiempo;
            // el personaje es opcional.
            e.HasOne<Proyecto>()
                .WithMany()
                .HasForeignKey(c => c.ProyectoId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<LineaTiempo>()
                .WithMany()
                .HasForeignKey(c => c.LineaId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<Personaje>()
                .WithMany()
                .HasForeignKey(c => c.PersonajeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LineaTiempo>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Nombre).IsRequired().HasMaxLength(100);

            // Cada línea pertenece a un único proyecto.
            e.HasOne<Proyecto>()
                .WithMany()
                .HasForeignKey(l => l.ProyectoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Nota>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Titulo).IsRequired().HasMaxLength(150);
            e.Property(n => n.Texto).HasMaxLength(5000);

            // Cada nota pertenece a un único proyecto.
            e.HasOne<Proyecto>()
                .WithMany()
                .HasForeignKey(n => n.ProyectoId)
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

        if (!ExisteTabla(conn, "Cronologias"))
        {
            using var create = conn.CreateCommand();
            create.CommandText = "CREATE TABLE \"Cronologias\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Cronologias\" PRIMARY KEY AUTOINCREMENT, \"ProyectoId\" INTEGER NOT NULL, \"LineaId\" INTEGER NOT NULL, \"PersonajeId\" INTEGER NULL, \"Titulo\" TEXT NOT NULL, \"Descripcion\" TEXT NOT NULL, \"Tipo\" TEXT NOT NULL, \"Orden\" INTEGER NOT NULL);";
            create.ExecuteNonQuery();
        }

        if (!ExisteTabla(conn, "Notas"))
        {
            using var create = conn.CreateCommand();
            create.CommandText = "CREATE TABLE \"Notas\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Notas\" PRIMARY KEY AUTOINCREMENT, \"ProyectoId\" INTEGER NOT NULL, \"Titulo\" TEXT NOT NULL, \"Texto\" TEXT NOT NULL, \"FechaCreacion\" TEXT NOT NULL);";
            create.ExecuteNonQuery();
        }

        if (!ExisteColumna(conn, "Cronologias", "Tipo"))
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE Cronologias ADD COLUMN Tipo TEXT NOT NULL DEFAULT 'BEAT';";
            alter.ExecuteNonQuery();
        }

        if (!ExisteColumna(conn, "Cronologias", "Orden"))
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE Cronologias ADD COLUMN Orden INTEGER NOT NULL DEFAULT 0;";
            alter.ExecuteNonQuery();
        }

        if (!ExisteColumna(conn, "Cronologias", "NotaId"))
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE Cronologias ADD COLUMN NotaId INTEGER NULL;";
            alter.ExecuteNonQuery();
        }

        if (!ExisteTabla(conn, "LineasTiempo"))
        {
            using var create = conn.CreateCommand();
            create.CommandText = "CREATE TABLE \"LineasTiempo\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_LineasTiempo\" PRIMARY KEY AUTOINCREMENT, \"ProyectoId\" INTEGER NOT NULL, \"Nombre\" TEXT NOT NULL, \"Orden\" INTEGER NOT NULL);";
            create.ExecuteNonQuery();
        }

        if (!ExisteColumna(conn, "Cronologias", "LineaId"))
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE Cronologias ADD COLUMN LineaId INTEGER NOT NULL DEFAULT 0;";
            alter.ExecuteNonQuery();
        }

        // El canvas libre se sustituye por carriles: se quitan las coordenadas.
        foreach (string col in new[] { "PosX", "PosY" })
        {
            if (ExisteColumna(conn, "Cronologias", col))
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = $"ALTER TABLE Cronologias DROP COLUMN {col};";
                alter.ExecuteNonQuery();
            }
        }

        // Cada proyecto necesita al menos un carril; los eventos antiguos
        // (LineaId = 0) pasan al primer carril de su proyecto.
        AsegurarLineasPorDefecto(conn);

        // La fecha se eliminó del modelo: se quita la columna si existe.
        if (ExisteColumna(conn, "Cronologias", "Fecha"))
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE Cronologias DROP COLUMN Fecha;";
            alter.ExecuteNonQuery();
        }

        if (!ExisteColumna(conn, "Personajes", "ProyectoId"))
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE Personajes ADD COLUMN ProyectoId INTEGER NOT NULL DEFAULT 0;";
            alter.ExecuteNonQuery();
        }

        if (!ExisteColumna(conn, "Personajes", "Grupo"))
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE Personajes ADD COLUMN Grupo TEXT NOT NULL DEFAULT '';";
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

    // Cada proyecto necesita al menos un carril; los eventos sin carril
    // (LineaId = 0) pasan al primer carril de su proyecto.
    private static void AsegurarLineasPorDefecto(System.Data.Common.DbConnection conn)
    {
        var proyectos = new List<long>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id FROM Proyectos;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                proyectos.Add(Convert.ToInt64(reader.GetValue(0)));
        }

        foreach (long proyectoId in proyectos)
        {
            long lineaId;
            using (var sel = conn.CreateCommand())
            {
                sel.CommandText = "SELECT MIN(Id) FROM LineasTiempo WHERE ProyectoId = @proy;";
                var p = sel.CreateParameter();
                p.ParameterName = "@proy";
                p.Value = proyectoId;
                sel.Parameters.Add(p);
                object? primera = sel.ExecuteScalar();
                if (primera != null && primera != DBNull.Value)
                {
                    lineaId = Convert.ToInt64(primera);
                }
                else
                {
                    using var ins = conn.CreateCommand();
                    ins.CommandText = "INSERT INTO LineasTiempo (ProyectoId, Nombre, Orden) VALUES (@proy, 'Línea principal', 1);";
                    var q = ins.CreateParameter();
                    q.ParameterName = "@proy";
                    q.Value = proyectoId;
                    ins.Parameters.Add(q);
                    ins.ExecuteNonQuery();

                    using var id = conn.CreateCommand();
                    id.CommandText = "SELECT last_insert_rowid();";
                    lineaId = Convert.ToInt64(id.ExecuteScalar());
                }
            }

            using var upd = conn.CreateCommand();
            upd.CommandText = "UPDATE Cronologias SET LineaId = @lin WHERE ProyectoId = @proy AND LineaId = 0;";
            var a = upd.CreateParameter();
            a.ParameterName = "@lin";
            a.Value = lineaId;
            upd.Parameters.Add(a);
            var b = upd.CreateParameter();
            b.ParameterName = "@proy";
            b.Value = proyectoId;
            upd.Parameters.Add(b);
            upd.ExecuteNonQuery();
        }
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
