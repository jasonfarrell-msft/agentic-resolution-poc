using AgenticResolution.Api.Data;
using AgenticResolution.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable
#pragma warning disable CS0612

namespace AgenticResolution.Api.Migrations;

[DbContext(typeof(AppDbContext))]
partial class AppDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 128);
        modelBuilder.UseIdentityColumns(1L);

        modelBuilder.Entity("AgenticResolution.Api.Models.Ticket", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uniqueidentifier")
                .HasDefaultValueSql("NEWSEQUENTIALID()");
            b.Property<string>("AgentAction").HasMaxLength(100).HasColumnType("nvarchar(100)");
            b.Property<double?>("AgentConfidence").HasColumnType("float");
            b.Property<string>("AssignedTo").HasMaxLength(100).HasColumnType("nvarchar(100)");
            b.Property<string>("Caller").IsRequired().HasMaxLength(100).HasColumnType("nvarchar(100)");
            b.Property<string>("Category").IsRequired().HasMaxLength(100).HasColumnType("nvarchar(100)");
            b.Property<string>("Classification").HasMaxLength(20).HasColumnType("nvarchar(20)");
            b.Property<DateTime>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");
            b.Property<string>("Description").HasColumnType("nvarchar(max)");
            b.Property<string>("MatchedTicketNumber").HasMaxLength(20).HasColumnType("nvarchar(20)");
            b.Property<string>("Number").IsRequired().HasMaxLength(15).HasColumnType("nvarchar(15)");
            b.Property<int>("Priority").HasColumnType("int");
            b.Property<string>("ResolutionNotes").HasColumnType("nvarchar(max)");
            b.Property<string>("ShortDescription").IsRequired().HasMaxLength(200).HasColumnType("nvarchar(200)");
            b.Property<int>("State").HasColumnType("int");
            b.Property<DateTime>("UpdatedAt").ValueGeneratedOnAdd().HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");
            b.HasKey("Id");
            b.HasIndex("CreatedAt");
            b.HasIndex("Number").IsUnique();
            b.HasIndex("State");
            b.ToTable("Tickets");
        });

        modelBuilder.Entity("AgenticResolution.Api.Models.TicketNumberSequence", b =>
        {
            b.Property<int>("Id").HasColumnType("int");
            b.Property<long>("LastValue").HasColumnType("bigint");
            b.HasKey("Id");
            b.ToTable("TicketNumberSequences");
            b.HasData(new { Id = 1, LastValue = 10000L });
        });
    }
}
