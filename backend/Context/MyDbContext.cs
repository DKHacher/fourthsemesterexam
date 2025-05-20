using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using backend.Entities;

namespace backend.Context;

public partial class MyDbContext : DbContext
{
    public MyDbContext()
    {
    }

    public MyDbContext(DbContextOptions<MyDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Storeddatum> Storeddata { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseNpgsql("Server=localhost;Database=database;Port=5432;User Id =testuser;Password=testpass;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Storeddatum>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("storeddata_pkey");

            entity.ToTable("storeddata");

            entity.Property(e => e.Id)
                .HasMaxLength(999)
                .HasColumnName("id");
            entity.Property(e => e.Date)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("date");
            entity.Property(e => e.Deviceid)
                .HasMaxLength(999)
                .HasColumnName("deviceid");
            entity.Property(e => e.Linktopicture)
                .HasMaxLength(999)
                .HasColumnName("linktopicture");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
