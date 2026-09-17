namespace OrderPlatform.Migrations.Tests;

/// <summary>Proves each ADR-0009 rule with a script that must be rejected and one that must be accepted.</summary>
public sealed class MigrationRuleTests
{
    private const string Lock = "SET lock_timeout = '5s';\n";

    public static TheoryData<string, string> RejectedExpandScripts => new()
    {
        { "removes something", Lock + "ALTER TABLE billing.invoices DROP COLUMN legacy_reference;" },
        { "removes something", Lock + "DROP TABLE IF EXISTS billing.old_invoices;" },
        { "renames", Lock + "ALTER TABLE billing.invoices RENAME COLUMN number TO invoice_number;" },
        { "changes a column type", Lock + "ALTER TABLE billing.invoices ALTER COLUMN amount TYPE numeric(19,4);" },
        { "volatile default", Lock + "ALTER TABLE billing.invoices ADD COLUMN correlation_id uuid DEFAULT gen_random_uuid();" },
        { "volatile default", Lock + "ALTER TABLE billing.invoices ADD COLUMN IF NOT EXISTS imported_at timestamptz NOT NULL DEFAULT now();" },
        { "volatile default", Lock + "ALTER TABLE billing.invoices ADD COLUMN sequence_no bigint GENERATED ALWAYS AS IDENTITY;" },
        { "NOT NULL column without a DEFAULT", Lock + "ALTER TABLE billing.invoices ADD COLUMN currency text NOT NULL;" },
        { "SET NOT NULL without", Lock + "ALTER TABLE billing.invoices ALTER COLUMN currency SET NOT NULL;" },
        { "validated constraint", Lock + "ALTER TABLE billing.invoices ADD CONSTRAINT fk_account FOREIGN KEY (account_id) REFERENCES customers.accounts (id);" },
        { "without USING INDEX", Lock + "ALTER TABLE billing.invoices ADD CONSTRAINT uq_number UNIQUE (number);" },
        { "without CONCURRENTLY", Lock + "CREATE INDEX IF NOT EXISTS ix_invoices_account ON billing.invoices (account_id);" },
        { "no lock_timeout", "CREATE TABLE IF NOT EXISTS billing.refunds (id uuid PRIMARY KEY);" },
        { "no explicit BEGIN", Lock + "CREATE TABLE IF NOT EXISTS billing.a (id uuid PRIMARY KEY);\nCREATE TABLE IF NOT EXISTS billing.b (id uuid PRIMARY KEY);" },
        { "must be the only statement", Lock + "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_a ON billing.invoices (a);\nCREATE INDEX CONCURRENTLY IF NOT EXISTS ix_b ON billing.invoices (b);" },
        { "must be the only statement", Lock + "BEGIN;\nCREATE INDEX CONCURRENTLY IF NOT EXISTS ix_a ON billing.invoices (a);\nCOMMIT;" },
    };

    public static TheoryData<string, string> AcceptedExpandScripts => new()
    {
        { "new table with volatile defaults and index", Lock + "BEGIN;\nCREATE TABLE IF NOT EXISTS billing.refunds (\n  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),\n  created_at timestamptz NOT NULL DEFAULT now()\n);\nCREATE INDEX IF NOT EXISTS ix_refunds_created ON billing.refunds (created_at);\nCOMMIT;" },
        { "nullable column", Lock + "ALTER TABLE billing.invoices ADD COLUMN IF NOT EXISTS external_reference text;" },
        { "not null with constant default", Lock + "ALTER TABLE billing.invoices ADD COLUMN IF NOT EXISTS currency text NOT NULL DEFAULT 'EUR';" },
        { "constraint not valid", Lock + "ALTER TABLE billing.invoices ADD CONSTRAINT fk_account FOREIGN KEY (account_id) REFERENCES customers.accounts (id) NOT VALID;" },
        { "concurrent index alone", Lock + "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_invoices_account ON billing.invoices (account_id);" },
        { "keywords inside comments, literals and function bodies", Lock + "-- we never DROP COLUMN here\nCREATE OR REPLACE FUNCTION billing.describe() RETURNS text LANGUAGE sql AS $body$ select 'rename or drop table' $body$;" },
    };

    [Theory]
    [MemberData(nameof(RejectedExpandScripts))]
    public void Rejects_unsafe_expand_script(string expectedViolation, string sql)
    {
        var script = new MigrationScript("Billing.Infrastructure", ScriptKind.Expand, "0002_change.sql", sql);

        var violation = MigrationRules.CheckScript(script, [script]).ShouldHaveSingleItem();
        violation.ShouldContain(expectedViolation);
    }

    [Theory]
    [MemberData(nameof(AcceptedExpandScripts))]
    public void Accepts_safe_expand_script(string rule, string sql)
    {
        var script = new MigrationScript("Billing.Infrastructure", ScriptKind.Expand, "0002_change.sql", sql);

        MigrationRules.CheckScript(script, [script]).ShouldBeEmpty($"rule: {rule}");
    }

    [Fact]
    public void Accepts_set_not_null_after_a_validated_check_constraint()
    {
        var check = new MigrationScript("Billing.Infrastructure", ScriptKind.Expand, "0003_currency_check.sql",
            Lock + "ALTER TABLE billing.invoices ADD CONSTRAINT ck_currency_not_null CHECK (currency IS NOT NULL) NOT VALID;");
        var validate = new MigrationScript("Billing.Infrastructure", ScriptKind.Expand, "0004_validate_currency_check.sql",
            Lock + "ALTER TABLE billing.invoices VALIDATE CONSTRAINT ck_currency_not_null;");
        var setNotNull = new MigrationScript("Billing.Infrastructure", ScriptKind.Expand, "0005_currency_not_null.sql",
            Lock + "ALTER TABLE billing.invoices ALTER COLUMN currency SET NOT NULL;");

        MigrationRules.CheckScript(setNotNull, [check, validate, setNotNull]).ShouldBeEmpty();
    }

    [Fact]
    public void Contract_script_may_drop_when_it_references_its_expand_script()
    {
        var expand = new MigrationScript("Billing.Infrastructure", ScriptKind.Expand, "0002_add_invoice_number.sql",
            Lock + "ALTER TABLE billing.invoices ADD COLUMN IF NOT EXISTS invoice_number text;");
        var contract = new MigrationScript("Billing.Infrastructure", ScriptKind.Contract, "0007_drop_number.sql",
            "-- completes: expand/0002_add_invoice_number.sql\n" + Lock + "ALTER TABLE billing.invoices DROP COLUMN IF EXISTS number;");

        MigrationRules.CheckScript(contract, [expand, contract]).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("-- no reference\n" + Lock + "ALTER TABLE billing.invoices DROP COLUMN IF EXISTS number;")]
    [InlineData("-- completes: expand/0099_missing.sql\n" + Lock + "ALTER TABLE billing.invoices DROP COLUMN IF EXISTS number;")]
    public void Rejects_contract_script_without_a_valid_expand_reference(string sql)
    {
        var contract = new MigrationScript("Billing.Infrastructure", ScriptKind.Contract, "0007_drop_number.sql", sql);

        MigrationRules.CheckScript(contract, [contract]).ShouldNotBeEmpty();
    }

    [Fact]
    public void Generated_patch_rules_accept_Marten_table_creation_and_reject_destructive_changes()
    {
        const string created = """
            CREATE TABLE IF NOT EXISTS ordering.mt_doc_refund (
                id uuid NOT NULL, data jsonb NOT NULL,
                mt_version uuid NOT NULL DEFAULT (md5(random()::text || clock_timestamp()::text)::uuid));
            CREATE INDEX mt_doc_refund_idx_created ON ordering.mt_doc_refund USING btree ((data ->> 'CreatedAt'));
            DROP FUNCTION IF EXISTS ordering.mt_upsert_refund(jsonb, varchar, uuid) CASCADE;
            """;
        const string destructive = """
            ALTER TABLE ordering.mt_doc_idempotencyrecord ADD COLUMN mt_deleted boolean NULL DEFAULT (random() > 1);
            CREATE INDEX mt_doc_idempotencyrecord_idx_key ON ordering.mt_doc_idempotencyrecord USING btree ((data ->> 'Key'));
            ALTER TABLE ordering.mt_doc_idempotencyrecord DROP COLUMN mt_dotnet_type;
            """;

        MigrationRules.CheckGeneratedPatch(created).ShouldBeEmpty();
        MigrationRules.CheckGeneratedPatch(destructive).Count.ShouldBe(3);
    }
}
