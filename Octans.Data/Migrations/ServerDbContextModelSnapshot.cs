﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Octans.Core.Models;

#nullable disable

namespace Octans.Server.Migrations
{
    [DbContext(typeof(ServerDbContext))]
    partial class ServerDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "9.0.2");

            modelBuilder.Entity("Octans.Core.Downloaders.DownloadStatus", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<long>("BytesDownloaded")
                        .HasColumnType("INTEGER");

                    b.Property<DateTime?>("CompletedAt")
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("TEXT");

                    b.Property<double>("CurrentSpeed")
                        .HasColumnType("REAL");

                    b.Property<string>("DestinationPath")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Domain")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("ErrorMessage")
                        .HasColumnType("TEXT");

                    b.Property<string>("Filename")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("LastUpdated")
                        .HasColumnType("TEXT");

                    b.Property<DateTime?>("StartedAt")
                        .HasColumnType("TEXT");

                    b.Property<int>("State")
                        .HasColumnType("INTEGER");

                    b.Property<long>("TotalBytes")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Url")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("DownloadStatuses");
                });

            modelBuilder.Entity("Octans.Core.Downloaders.QueuedDownload", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("DestinationPath")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Domain")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("Priority")
                        .HasColumnType("INTEGER");

                    b.Property<DateTime>("QueuedAt")
                        .HasColumnType("TEXT");

                    b.Property<string>("Url")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("QueuedDownloads");
                });

            modelBuilder.Entity("Octans.Core.Models.FileRecord", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("Filepath")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("FileRecords");
                });

            modelBuilder.Entity("Octans.Core.Models.HashItem", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<DateTime?>("DeletedAt")
                        .HasColumnType("TEXT");

                    b.Property<byte[]>("Hash")
                        .IsRequired()
                        .HasColumnType("BLOB");

                    b.HasKey("Id");

                    b.ToTable("Hashes");
                });

            modelBuilder.Entity("Octans.Core.Models.Tagging.Mapping", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int>("HashId")
                        .HasColumnType("INTEGER");

                    b.Property<int>("TagId")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("HashId");

                    b.HasIndex("TagId");

                    b.ToTable("Mappings");
                });

            modelBuilder.Entity("Octans.Core.Models.Tagging.Namespace", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("Value")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("Namespaces");
                });

            modelBuilder.Entity("Octans.Core.Models.Tagging.Subtag", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("Value")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("Subtags");
                });

            modelBuilder.Entity("Octans.Core.Models.Tagging.Tag", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int>("NamespaceId")
                        .HasColumnType("INTEGER");

                    b.Property<int>("SubtagId")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("NamespaceId");

                    b.HasIndex("SubtagId");

                    b.ToTable("Tags");
                });

            modelBuilder.Entity("Octans.Core.Models.Tagging.TagParent", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int>("ChildId")
                        .HasColumnType("INTEGER");

                    b.Property<int>("ParentId")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Status")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("ChildId");

                    b.HasIndex("ParentId");

                    b.ToTable("TagParents");
                });

            modelBuilder.Entity("Octans.Core.Models.Tagging.TagSibling", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int>("BetterId")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Status")
                        .HasColumnType("INTEGER");

                    b.Property<int>("WorseId")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("BetterId");

                    b.HasIndex("WorseId");

                    b.ToTable("TagSiblings");
                });

            modelBuilder.Entity("Octans.Core.Models.Tagging.Mapping", b =>
                {
                    b.HasOne("Octans.Core.Models.HashItem", "Hash")
                        .WithMany()
                        .HasForeignKey("HashId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Octans.Core.Models.Tagging.Tag", "Tag")
                        .WithMany()
                        .HasForeignKey("TagId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Hash");

                    b.Navigation("Tag");
                });

            modelBuilder.Entity("Octans.Core.Models.Tagging.Tag", b =>
                {
                    b.HasOne("Octans.Core.Models.Tagging.Namespace", "Namespace")
                        .WithMany()
                        .HasForeignKey("NamespaceId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Octans.Core.Models.Tagging.Subtag", "Subtag")
                        .WithMany()
                        .HasForeignKey("SubtagId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Namespace");

                    b.Navigation("Subtag");
                });

            modelBuilder.Entity("Octans.Core.Models.Tagging.TagParent", b =>
                {
                    b.HasOne("Octans.Core.Models.Tagging.Tag", "Child")
                        .WithMany()
                        .HasForeignKey("ChildId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Octans.Core.Models.Tagging.Tag", "Parent")
                        .WithMany()
                        .HasForeignKey("ParentId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Child");

                    b.Navigation("Parent");
                });

            modelBuilder.Entity("Octans.Core.Models.Tagging.TagSibling", b =>
                {
                    b.HasOne("Octans.Core.Models.Tagging.Tag", "Better")
                        .WithMany()
                        .HasForeignKey("BetterId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Octans.Core.Models.Tagging.Tag", "Worse")
                        .WithMany()
                        .HasForeignKey("WorseId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Better");

                    b.Navigation("Worse");
                });
#pragma warning restore 612, 618
        }
    }
}
