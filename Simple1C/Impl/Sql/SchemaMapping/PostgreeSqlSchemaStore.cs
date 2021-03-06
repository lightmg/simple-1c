﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Simple1C.Impl.Helpers;
using Simple1C.Impl.Sql.SqlAccess;

namespace Simple1C.Impl.Sql.SchemaMapping
{
    internal class PostgreeSqlSchemaStore : IMappingSource
    {
        private readonly PostgreeSqlDatabase database;

        private readonly Dictionary<string, TableMapping> byQueryNameCache =
            new Dictionary<string, TableMapping>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, TableMapping> byDbNameCache =
            new Dictionary<string, TableMapping>(StringComparer.OrdinalIgnoreCase);

        private static readonly TableDesc typeMappingsTableDesc =
            new TableDesc
            {
                tableName = "simple1c.tableMappings",
                columns = new[]
                {
                    new DataColumn
                    {
                        ColumnName = "queryTableName",
                        AllowDBNull = false,
                        DataType = typeof(string),
                        MaxLength = 200
                    },
                    new DataColumn
                    {
                        ColumnName = "dbName",
                        AllowDBNull = false,
                        DataType = typeof(string),
                        MaxLength = 200
                    },
                    new DataColumn
                    {
                        ColumnName = "type",
                        AllowDBNull = false,
                        DataType = typeof(string),
                        MaxLength = 50
                    },
                    new DataColumn
                    {
                        ColumnName = "properties",
                        AllowDBNull = false,
                        DataType = typeof(string),
                        MaxLength = -1
                    }
                },
                createIndexesSql =
                    "CREATE INDEX tableMappings_index ON simple1c.tableMappings ((lower(queryTableName)))"
            };

        private static readonly TableDesc enumMappingsTableDesc =
            new TableDesc
            {
                tableName = "simple1c.enumMappings",
                columns = new[]
                {
                    new DataColumn
                    {
                        ColumnName = "enumName",
                        AllowDBNull = false,
                        DataType = typeof(string),
                        MaxLength = 200
                    },
                    new DataColumn
                    {
                        ColumnName = "enumValueName",
                        AllowDBNull = false,
                        DataType = typeof(string),
                        MaxLength = 200
                    },
                    new DataColumn
                    {
                        ColumnName = "orderIndex",
                        AllowDBNull = false,
                        DataType = typeof(int)
                    }
                },
                createIndexesSql =
                    "CREATE INDEX enumMappings_index ON simple1c.enumMappings (enumName,orderIndex)"
            };

        public PostgreeSqlSchemaStore(PostgreeSqlDatabase database)
        {
            this.database = database;
        }

        public void WriteEnumMappings(EnumMapping[] mappings)
        {
            RecreateTable(enumMappingsTableDesc, mappings, x => new object[]
            {
                x.enumName,
                x.enumValueName,
                x.orderIndex
            });
        }

        public void WriteTableMappings(TableMapping[] mappings)
        {
            RecreateTable(typeMappingsTableDesc, mappings, x => new object[]
            {
                x.QueryTableName,
                x.DbTableName,
                x.Type.ToString(),
                x.Properties.Select(p => p.Serialize()).JoinStrings("\r\n")
            });
        }

        public TableMapping ResolveTableOrNull(string queryName)
        {
            TableMapping result;
            if (!byQueryNameCache.TryGetValue(queryName, out result))
                byQueryNameCache.Add(queryName, result = LoadMappingOrNull(queryName));
            return result;
        }

        public TableMapping ResolveTableByDbNameOrNull(string dbName)
        {
            TableMapping result;
            if (!byDbNameCache.TryGetValue(dbName, out result))
                byDbNameCache.Add(dbName, result = GetMappingByDbName(dbName));
            return result;
        }

        private const string SqlByDbName = "select queryTableName,dbName,type,properties " +
                                   "from simple1c.tableMappings " +
                                   "where lower(dbName) = lower(@p0)";

        private const string SqlByQueryName = "select queryTableName,dbName,type,properties " +
                                      "from simple1c.tableMappings " +
                                      "where lower(queryTableName) = lower(@p0)";

        private TableMapping LoadMappingOrNull(string queryName)
        {
            return GetMappingByQuery(SqlByQueryName, queryName);
        }

        private TableMapping GetMappingByDbName(string dbName)
        {
            return GetMappingByQuery(SqlByDbName, dbName);
        }

        private TableMapping GetMappingByQuery(string sql, string name)
        {
            return database.ExecuteEnumerable(
                    sql, new object[] {name.ToLower()},
                    r => new TableMapping(r.GetString(0),
                        r.GetString(1),
                        TableMapping.ParseTableType(r.GetString(2)),
                        r.GetString(3)
                            .Split(new[] {"\r\n"}, StringSplitOptions.RemoveEmptyEntries)
                            .Select(PropertyMapping.Parse).ToArray()))
                .SingleOrDefault();
        }

        private void RecreateTable<T>(TableDesc tableDesc, IEnumerable<T> data, Func<T, object[]> getColumnValues)
        {
            database.CreateTable(tableDesc.tableName, tableDesc.columns);
            database.BulkCopy(tableDesc.tableName, tableDesc.columns, data.Select(delegate(T x)
            {
                var columnValues = getColumnValues(x);
                if (columnValues.Length != tableDesc.columns.Length)
                {
                    const string messageFormat = "invalid values, expected length [{0}], actual length [{1}]";
                    throw new InvalidOperationException(string.Format(messageFormat,
                        tableDesc.columns.Length, columnValues.Length));
                }

                return columnValues;
            }));
            database.ExecuteNonQuery(tableDesc.createIndexesSql);
        }

        private class TableDesc
        {
            public string tableName;
            public DataColumn[] columns;
            public string createIndexesSql;
        }
    }
}