using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace IronMonkey.Data.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class ProductsAndQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "opportunities",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "opportunities",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "opportunities",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExchangeRateDate",
                table: "opportunities",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OneOffAmount",
                table: "opportunities",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "PriceListId",
                table: "opportunities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RecurringAmount",
                table: "opportunities",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                table: "opportunities",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "price_lists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_lists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ChargeType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BillingFrequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DefaultPeriods = table.Column<int>(type: "integer", nullable: false),
                    UnitOfMeasure = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DefaultTaxRatePercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    custom_field_values = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "quote_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NumberPrefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DefaultValidityDays = table.Column<int>(type: "integer", nullable: false),
                    DefaultTerms = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    ApprovalDiscountThresholdPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    ShareLinkLifetimeDays = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quote_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "quotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    NumberPrefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: false),
                    Terms = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    RecipientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecipientEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxTotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    OneOffTotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    RecurringTotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    MaxDiscountPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    RequiresApproval = table.Column<bool>(type: "boolean", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupersedesQuoteId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupersededByQuoteId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RespondedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ResponseNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quotes_opportunities_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "opportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "opportunity_line_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductPriceId = table.Column<Guid>(type: "uuid", nullable: true),
                    PriceListId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    ChargeType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BillingFrequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Periods = table.Column<int>(type: "integer", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    TaxRatePercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    IsMigrated = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opportunity_line_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_opportunity_line_items_opportunities_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "opportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_opportunity_line_items_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_prices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceListId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_prices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_prices_price_lists_PriceListId",
                        column: x => x.PriceListId,
                        principalTable: "price_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_prices_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quote_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductPriceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ChargeType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BillingFrequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Periods = table.Column<int>(type: "integer", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    TaxRatePercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quote_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quote_lines_quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quote_share_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastViewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ViewCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quote_share_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quote_share_links_quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quote_status_changes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quote_status_changes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quote_status_changes_quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 21, "quotes:approve" },
                    { 22, "catalog:write" }
                });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "PermissionId", "RoleId" },
                values: new object[,]
                {
                    { 21, 1 },
                    { 22, 1 },
                    { 21, 201 },
                    { 22, 201 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_PriceListId",
                table: "opportunities",
                column: "PriceListId");

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_line_items_OpportunityId",
                table: "opportunity_line_items",
                column: "OpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_line_items_ProductId",
                table: "opportunity_line_items",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_line_items_TenantId_OpportunityId_Position",
                table: "opportunity_line_items",
                columns: new[] { "TenantId", "OpportunityId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_line_items_TenantId_ProductId",
                table: "opportunity_line_items",
                columns: new[] { "TenantId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_price_lists_TenantId_Default",
                table: "price_lists",
                column: "TenantId",
                unique: true,
                filter: "\"IsDefault\" = true AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_product_prices_PriceListId",
                table: "product_prices",
                column: "PriceListId");

            migrationBuilder.CreateIndex(
                name: "IX_product_prices_Product_List_EffectiveFrom",
                table: "product_prices",
                columns: new[] { "TenantId", "ProductId", "PriceListId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_prices_ProductId",
                table: "product_prices",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_products_TenantId_Category",
                table: "products",
                columns: new[] { "TenantId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_quote_lines_QuoteId",
                table: "quote_lines",
                column: "QuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_quote_lines_TenantId_QuoteId_Position",
                table: "quote_lines",
                columns: new[] { "TenantId", "QuoteId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_quote_settings_TenantId",
                table: "quote_settings",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quote_share_links_QuoteId",
                table: "quote_share_links",
                column: "QuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_quote_share_links_TenantId_QuoteId",
                table: "quote_share_links",
                columns: new[] { "TenantId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_quote_share_links_TokenHash",
                table: "quote_share_links",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quote_status_changes_QuoteId",
                table: "quote_status_changes",
                column: "QuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_quote_status_changes_TenantId_QuoteId_ChangedAt",
                table: "quote_status_changes",
                columns: new[] { "TenantId", "QuoteId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_quotes_OpportunityId",
                table: "quotes",
                column: "OpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_quotes_TenantId_Number_Version",
                table: "quotes",
                columns: new[] { "TenantId", "Number", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quotes_TenantId_OpportunityId",
                table: "quotes",
                columns: new[] { "TenantId", "OpportunityId" });

            migrationBuilder.AddForeignKey(
                name: "FK_opportunities_price_lists_PriceListId",
                table: "opportunities",
                column: "PriceListId",
                principalTable: "price_lists",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Product codes are unique per tenant case-insensitively ("SUV-01" and "suv-01"
            // are the same SKU to a person). EF cannot express an expression index.
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ""IX_products_TenantId_lower_Code""
                    ON products (""TenantId"", lower(""Code""))
                    WHERE ""IsDeleted"" = false;");

            // ── Backfill: every existing Amount becomes a line item ─────────────────────
            //
            // Deal value is now the sum of line items, so a deal with no lines is worth 0.
            // Without this, every opportunity created before line items would silently drop
            // to zero on the dashboard the moment its totals were next recomputed.
            //
            // EVERY opportunity gets exactly one migrated line, including those whose Amount
            // was 0. The schema cannot tell "zero" from "never entered" — Amount was
            // NOT NULL DEFAULT 0 — so both are carried over the same way: a zero-priced line
            // flagged IsMigrated. That keeps the rule uniform (each pre-existing deal has one
            // traceable carried-over line), leaves the total at exactly what it was, and gives
            // the UI something to label "value not recorded before line items" rather than
            // presenting an empty deal as though someone had deliberately priced it at nothing.
            //
            // The value is copied verbatim (no currency rounding) — the migration preserves,
            // it does not reinterpret. OneOffAmount mirrors Amount because a lump sum is, by
            // the only reading available, a one-off figure.
            migrationBuilder.Sql(@"
                INSERT INTO opportunity_line_items
                    (""Id"", ""TenantId"", ""OpportunityId"", ""Position"", ""Description"",
                     ""Quantity"", ""UnitPrice"", ""ChargeType"", ""BillingFrequency"", ""Periods"",
                     ""DiscountPercent"", ""TaxRatePercent"",
                     ""GrossAmount"", ""DiscountAmount"", ""NetAmount"", ""TaxAmount"", ""TotalAmount"",
                     ""IsMigrated"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                SELECT gen_random_uuid(), o.""TenantId"", o.""Id"", 1,
                       'Deal value (carried over from before line items)',
                       1, o.""Amount"", 'OneOff', 'None', 1,
                       0, 0,
                       o.""Amount"", 0, o.""Amount"", 0, o.""Amount"",
                       true, now(), now(), false
                FROM opportunities o
                WHERE NOT EXISTS (
                    SELECT 1 FROM opportunity_line_items l WHERE l.""OpportunityId"" = o.""Id"");

                UPDATE opportunities SET ""OneOffAmount"" = ""Amount"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_opportunities_price_lists_PriceListId",
                table: "opportunities");

            migrationBuilder.DropTable(
                name: "opportunity_line_items");

            migrationBuilder.DropTable(
                name: "product_prices");

            migrationBuilder.DropTable(
                name: "quote_lines");

            migrationBuilder.DropTable(
                name: "quote_settings");

            migrationBuilder.DropTable(
                name: "quote_share_links");

            migrationBuilder.DropTable(
                name: "quote_status_changes");

            migrationBuilder.DropTable(
                name: "price_lists");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "quotes");

            migrationBuilder.DropIndex(
                name: "IX_opportunities_PriceListId",
                table: "opportunities");

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { 21, 1 });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { 22, 1 });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { 21, 201 });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { 22, 201 });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "Id",
                keyValue: 21);

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "Id",
                keyValue: 22);

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "ExchangeRateDate",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "OneOffAmount",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "PriceListId",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "RecurringAmount",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "TaxAmount",
                table: "opportunities");
        }
    }
}
