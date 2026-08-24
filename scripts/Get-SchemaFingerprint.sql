-- Generates the canonical whole-schema fingerprint used by the guarded migration
-- adoption scripts. Run against a database created at the relevant baseline migration.
SET NOCOUNT ON;

;WITH SchemaItems AS
(
    SELECT CONCAT(N'T|', s.name, N'|', t.name) AS Item
    FROM sys.tables AS t
    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    WHERE t.is_ms_shipped = 0 AND t.name <> N'__EFMigrationsHistory'

    UNION ALL

    SELECT CONCAT(
        N'C|', s.name, N'|', t.name, N'|', RIGHT(N'00000' + CONVERT(nvarchar(5), c.column_id), 5),
        N'|', c.name, N'|', ty.name, N'|', c.max_length, N'|', c.precision, N'|', c.scale,
        N'|', c.is_nullable, N'|', c.is_identity, N'|', c.is_computed, N'|', COALESCE(c.collation_name, N''))
    FROM sys.tables AS t
    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    INNER JOIN sys.columns AS c ON c.object_id = t.object_id
    INNER JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
    WHERE t.is_ms_shipped = 0 AND t.name <> N'__EFMigrationsHistory'

    UNION ALL

    SELECT CONCAT(
        N'I|', s.name, N'|', t.name, N'|', i.name, N'|', i.type_desc,
        N'|', i.is_unique, N'|', i.is_primary_key, N'|', i.is_unique_constraint,
        N'|', COALESCE(i.filter_definition, N''))
    FROM sys.tables AS t
    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    INNER JOIN sys.indexes AS i ON i.object_id = t.object_id
    WHERE t.is_ms_shipped = 0 AND t.name <> N'__EFMigrationsHistory' AND i.index_id > 0

    UNION ALL

    SELECT CONCAT(
        N'IC|', s.name, N'|', t.name, N'|', i.name, N'|', RIGHT(N'00000' + CONVERT(nvarchar(5), ic.index_column_id), 5),
        N'|', c.name, N'|', ic.key_ordinal, N'|', ic.is_descending_key, N'|', ic.is_included_column)
    FROM sys.tables AS t
    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    INNER JOIN sys.indexes AS i ON i.object_id = t.object_id
    INNER JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE t.is_ms_shipped = 0 AND t.name <> N'__EFMigrationsHistory' AND i.index_id > 0

    UNION ALL

    SELECT CONCAT(
        N'FK|', ps.name, N'|', pt.name, N'|', fk.name, N'|', rs.name, N'|', rt.name,
        N'|', fk.delete_referential_action, N'|', fk.update_referential_action)
    FROM sys.foreign_keys AS fk
    INNER JOIN sys.tables AS pt ON pt.object_id = fk.parent_object_id
    INNER JOIN sys.schemas AS ps ON ps.schema_id = pt.schema_id
    INNER JOIN sys.tables AS rt ON rt.object_id = fk.referenced_object_id
    INNER JOIN sys.schemas AS rs ON rs.schema_id = rt.schema_id
    WHERE pt.is_ms_shipped = 0 AND pt.name <> N'__EFMigrationsHistory'

    UNION ALL

    SELECT CONCAT(
        N'FKC|', ps.name, N'|', pt.name, N'|', fk.name, N'|', fkc.constraint_column_id,
        N'|', pc.name, N'|', rc.name)
    FROM sys.foreign_key_columns AS fkc
    INNER JOIN sys.foreign_keys AS fk ON fk.object_id = fkc.constraint_object_id
    INNER JOIN sys.tables AS pt ON pt.object_id = fk.parent_object_id
    INNER JOIN sys.schemas AS ps ON ps.schema_id = pt.schema_id
    INNER JOIN sys.columns AS pc ON pc.object_id = pt.object_id AND pc.column_id = fkc.parent_column_id
    INNER JOIN sys.columns AS rc ON rc.object_id = fk.referenced_object_id AND rc.column_id = fkc.referenced_column_id
    WHERE pt.is_ms_shipped = 0 AND pt.name <> N'__EFMigrationsHistory'

    UNION ALL

    SELECT CONCAT(N'D|', s.name, N'|', t.name, N'|', c.name, N'|', dc.definition)
    FROM sys.default_constraints AS dc
    INNER JOIN sys.tables AS t ON t.object_id = dc.parent_object_id
    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    INNER JOIN sys.columns AS c ON c.object_id = t.object_id AND c.column_id = dc.parent_column_id
    WHERE t.is_ms_shipped = 0 AND t.name <> N'__EFMigrationsHistory'

    UNION ALL

    SELECT CONCAT(N'CK|', s.name, N'|', t.name, N'|', cc.name, N'|', cc.definition)
    FROM sys.check_constraints AS cc
    INNER JOIN sys.tables AS t ON t.object_id = cc.parent_object_id
    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    WHERE t.is_ms_shipped = 0 AND t.name <> N'__EFMigrationsHistory'

    UNION ALL

    SELECT CONCAT(N'CC|', s.name, N'|', t.name, N'|', c.name, N'|', cc.definition, N'|', cc.is_persisted)
    FROM sys.computed_columns AS cc
    INNER JOIN sys.tables AS t ON t.object_id = cc.object_id
    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    INNER JOIN sys.columns AS c ON c.object_id = cc.object_id AND c.column_id = cc.column_id
    WHERE t.is_ms_shipped = 0 AND t.name <> N'__EFMigrationsHistory'
)
SELECT
    CONVERT(varchar(64), HASHBYTES(
        'SHA2_256',
        STRING_AGG(CONVERT(nvarchar(max), Item) COLLATE DATABASE_DEFAULT, NCHAR(10))
            WITHIN GROUP (ORDER BY Item COLLATE DATABASE_DEFAULT)), 2) AS SchemaFingerprint,
    COUNT_BIG(*) AS SchemaItemCount
FROM SchemaItems;
