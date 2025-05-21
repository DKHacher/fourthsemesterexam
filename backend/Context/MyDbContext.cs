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
                .HasColumnType("timestamp with time zone")
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
