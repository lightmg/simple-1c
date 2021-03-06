using System;
using System.Collections.Generic;
using System.Linq;
using Simple1C.Impl.Helpers;
using Simple1C.Impl.Sql.SchemaMapping;
using Simple1C.Impl.Sql.SqlAccess.Syntax;

namespace Simple1C.Impl.Sql.Translation.Visitors
{
    internal class QueryFunctionRewriter : SqlVisitor
    {
        private readonly IMappingSource mappingSource;

        public QueryFunctionRewriter(IMappingSource mappingSource)
        {
            this.mappingSource = mappingSource;
        }

        public override ISqlElement VisitQueryFunction(QueryFunctionExpression expression)
        {
            expression = (QueryFunctionExpression) base.VisitQueryFunction(expression);
            if (expression.KnownFunction == KnownQueryFunction.DateTime)
            {
                ExpectArgumentCount(expression, 3, 6);
                var dateTimeComponents = new int[expression.Arguments.Count];
                for (var i = 0; i < expression.Arguments.Count; i++)
                {
                    var literal = expression.Arguments[i] as LiteralExpression;
                    if (literal == null)
                    {
                        const string messageFormat = "expected DateTime function parameters " +
                                                     "to be literals, but was [{0}]";
                        throw new InvalidOperationException(string.Format(messageFormat,
                            expression.Arguments.JoinStrings(",")));
                    }

                    dateTimeComponents[i] = (int) literal.Value;
                }

                if (expression.Arguments.Count == 3)
                    return new CastExpression
                    {
                        Type = "date",
                        Expression = new LiteralExpression
                        {
                            Value = new DateTime(dateTimeComponents[0],
                                    dateTimeComponents[1],
                                    dateTimeComponents[2])
                                .ToString("yyyy-MM-dd")
                        }
                    };
                return new CastExpression
                {
                    Type = "timestamp",
                    Expression = new LiteralExpression
                    {
                        Value = new DateTime(dateTimeComponents[0],
                                dateTimeComponents[1],
                                dateTimeComponents[2],
                                dateTimeComponents[3],
                                dateTimeComponents[4],
                                dateTimeComponents[5])
                            .ToString("yyyy-MM-dd HH:mm:ss")
                    }
                };
            }

            if (expression.KnownFunction == KnownQueryFunction.Year)
            {
                ExpectArgumentCount(expression, 1);
                return new QueryFunctionExpression
                {
                    KnownFunction = KnownQueryFunction.SqlDatePart,
                    Arguments = new List<ISqlElement>
                    {
                        new LiteralExpression {Value = "year"},
                        expression.Arguments[0]
                    }
                };
            }

            if (expression.KnownFunction == KnownQueryFunction.Quarter)
            {
                ExpectArgumentCount(expression, 1);
                return new QueryFunctionExpression
                {
                    KnownFunction = KnownQueryFunction.SqlDatePart,
                    Arguments = new List<ISqlElement>
                    {
                        new LiteralExpression {Value = "quarter"},
                        expression.Arguments[0]
                    }
                };
            }

            if (expression.KnownFunction == KnownQueryFunction.Presentation)
            {
                ExpectArgumentCount(expression, 1);
                return expression.Arguments[0];
            }

            if (expression.KnownFunction == KnownQueryFunction.IsNull)
            {
                ExpectArgumentCount(expression, 2);
                return new CaseExpression
                {
                    Elements =
                    {
                        new CaseElement
                        {
                            Condition = new IsNullExpression {Argument = expression.Arguments[0]},
                            Value = expression.Arguments[1]
                        }
                    },
                    DefaultValue = expression.Arguments[0]
                };
            }

            if (expression.KnownFunction == KnownQueryFunction.Substring)
            {
                ExpectArgumentCount(expression, 3);
                return new QueryFunctionExpression
                {
                    KnownFunction = KnownQueryFunction.Substring,
                    Arguments =
                    {
                        new CastExpression
                        {
                            Type = "varchar",
                            Expression = expression.Arguments[0]
                        },
                        expression.Arguments[1],
                        expression.Arguments[2]
                    }
                };
            }

            if (expression.KnownFunction == KnownQueryFunction.SqlDateTrunc)
            {
                ExpectArgumentCount(expression, 2);
                return new QueryFunctionExpression
                {
                    KnownFunction = KnownQueryFunction.SqlDateTrunc,
                    Arguments = {expression.Arguments[1], expression.Arguments[0]}
                };
            }

            if (expression.KnownFunction == KnownQueryFunction.TypeIdentifier)
            {
                ExpectArgumentCount(expression, 1);
                var columnReferenceExpression = expression.Arguments[0] as ColumnReferenceExpression;
                if (columnReferenceExpression == null)
                {
                    const string messageFormat = "[{0}] function expected to have column reference as an argument";
                    throw new InvalidOperationException(string.Format(messageFormat, expression.KnownFunction));
                }

                return GetTypeColumnRef(columnReferenceExpression);
            }

            return expression;
        }

        private ISqlElement GetTypeColumnRef(ColumnReferenceExpression columnReferenceExpression)
        {
            var tableDeclarationClause = columnReferenceExpression.Table as TableDeclarationClause;
            if (tableDeclarationClause == null)
            {
                var subqueryDeclaration = columnReferenceExpression.Table as SubqueryTable;
                if (subqueryDeclaration == null)
                {
                    const string message =
                        "[{0}] function not supported for a {1} " +
                        "column reference, [{2}.{3}]. Table is of type [{4}]";
                    throw new InvalidOperationException(string.Format(
                        message, columnReferenceExpression.Table.GetType().Name, KnownQueryFunction.TypeIdentifier,
                        columnReferenceExpression.Table.Alias, columnReferenceExpression.Name,
                        columnReferenceExpression.Table.GetType().Name));
                }

                var columnAlias = columnReferenceExpression.Name + "_Type";

                foreach (var union in subqueryDeclaration.Query.Query.Unions)
                    AddTypeColumn(union.SelectClause, columnReferenceExpression, columnAlias);

                return new ColumnReferenceExpression()
                {
                    Name = columnAlias,
                    Table = columnReferenceExpression.Table
                };
            }

            var resolvedTableMapping = mappingSource.ResolveTableByDbNameOrNull(tableDeclarationClause.Name);
            if (resolvedTableMapping == null)
            {
                const string message =
                    "can't find table [{0}]  in column reference [{0}.{1}] for function [{2}]";
                throw new InvalidOperationException(string.Format(message,
                    columnReferenceExpression.Table.Alias, columnReferenceExpression.Name,
                    KnownQueryFunction.TypeIdentifier));
            }

            foreach (var mapping in resolvedTableMapping.Properties)
            {
                if (mapping.SingleLayout != null &&
                    mapping.SingleLayout.DbColumnName == columnReferenceExpression.Name)
                {
                    if (string.IsNullOrEmpty(mapping.SingleLayout.NestedTableName))
                    {
                        const string msgFormat =
                            "[{0}] function not supported for non-reference columns, [{1}.{2}]";
                        throw new InvalidOperationException(string.Format(msgFormat,
                            KnownQueryFunction.TypeIdentifier,
                            columnReferenceExpression.Table.Alias, columnReferenceExpression.Name));
                    }

                    var byDbName = mappingSource.ResolveTableOrNull(mapping.SingleLayout.NestedTableName);
                    if (byDbName == null || !byDbName.Index.HasValue)
                    {
                        const string message = "can't find mapping for [{0}] following column reference " +
                                               "[{1}.{2}] for function [{3}]";
                        throw new InvalidOperationException(string.Format(message,
                            mapping.SingleLayout.NestedTableName, columnReferenceExpression.Table.Alias,
                            columnReferenceExpression.Name, KnownQueryFunction.TypeIdentifier));
                    }

                    return new LiteralExpression()
                    {
                        SqlType = SqlType.ByteArray,
                        Value = byDbName.Index.Value
                    };
                }

                if (mapping.UnionLayout != null &&
                    mapping.UnionLayout.ReferenceColumnName == columnReferenceExpression.Name)
                    return new ColumnReferenceExpression()
                    {
                        Name = mapping.UnionLayout.TableIndexColumnName,
                        Table = columnReferenceExpression.Table
                    };
            }

            const string msg = "could not find columns [{1}.{2}] for function [{0}]";
            throw new InvalidOperationException(string.Format(msg, KnownQueryFunction.TypeIdentifier,
                columnReferenceExpression.Table.Alias, columnReferenceExpression.Name));
        }

        private void AddTypeColumn(SelectClause select, ColumnReferenceExpression columnReferenceExpression,
            string columnAlias)
        {
            var field = select.Fields.SingleOrDefault(x => x.Alias == columnReferenceExpression.Name);
            if (field == null)
                field = select.Fields
                    .SingleOrDefault(x => x.Expression is ColumnReferenceExpression ex
                                          && ex.Name == columnReferenceExpression.Name);
            if (field == null)
                throw new InvalidOperationException(
                    $"Can't find field  {columnReferenceExpression.Table.Alias}.{columnReferenceExpression.Name}");
            var columnRef = field.Expression as ColumnReferenceExpression;
            select.Fields.Add(new SelectFieldExpression
            {
                Alias = columnAlias,
                Expression = GetTypeColumnRef(columnRef)
            });
        }

        private static void ExpectArgumentCount(QueryFunctionExpression expression,
            int expectedCount, int? expectedCount2 = null)
        {
            var isOk = expression.Arguments.Count == expectedCount ||
                       (expectedCount2.HasValue && expression.Arguments.Count == expectedCount2.Value);
            if (isOk)
                return;
            const string messageFormat = "[{0}] function expected to have {1} arguments but was [{2}]";
            throw new InvalidOperationException(string.Format(messageFormat,
                expression.KnownFunction,
                expectedCount2.HasValue
                    ? "[" + expectedCount + "] or [" + expectedCount2 + "]"
                    : "exactly [" + expectedCount + "]",
                expression.Arguments.Count));
        }
    }
}