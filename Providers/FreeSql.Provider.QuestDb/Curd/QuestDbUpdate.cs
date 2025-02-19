﻿using FreeSql.Internal;
using FreeSql.Internal.Model;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FreeSql.QuestDb.Curd
{
    class QuestDbUpdate<T1> : Internal.CommonProvider.UpdateProvider<T1>
    {
        public QuestDbUpdate(IFreeSql orm, CommonUtils commonUtils, CommonExpression commonExpression, object dywhere)
            : base(orm, commonUtils, commonExpression, dywhere)
        {
        }

        internal string InternalTableAlias { get; set; }
        internal StringBuilder InternalSbSet => _set;
        internal StringBuilder InternalSbSetIncr => _setIncr;
        internal Dictionary<string, bool> InternalIgnore => _ignore;
        internal void InternalResetSource(List<T1> source) => _source = source;

        internal string InternalWhereCaseSource(string CsName, Func<string, string> thenValue) =>
            WhereCaseSource(CsName, thenValue);

        internal void InternalToSqlCaseWhenEnd(StringBuilder sb, ColumnInfo col) => ToSqlCaseWhenEnd(sb, col);

        private int InternelExecuteAffrows()
        {
            var sql = ToSql();
            var execAsync = RestAPIExtension.ExecAsync(sql).GetAwaiter().GetResult();
            var resultHash = new Hashtable();
            try
            {
                resultHash = JsonConvert.DeserializeObject<Hashtable>(execAsync);
            }
            catch
            {
                if (execAsync.Contains("401"))
                {
                    throw new Exception("请确认new FreeSqlBuilder().UseQuestDbRestAPI()中设置的用户名密码是否正确.");
                }
            }
            var ddl = resultHash["ddl"]?.ToString();
            var updated = Convert.ToInt32(resultHash["updated"]);
            return ddl?.ToLower() == "ok" ? updated : 0;
        }

        public override int ExecuteAffrows()
        {
            //如果设置了RestAPI中Url则走HTTP
            if (string.IsNullOrWhiteSpace(RestAPIExtension.BaseUrl))
            {
                return base.SplitExecuteAffrows(_batchRowsLimit > 0 ? _batchRowsLimit : 500,
                    _batchParameterLimit > 0 ? _batchParameterLimit : 3000);
            }
            return InternelExecuteAffrows();
        }

        public override List<T1> ExecuteUpdated() => base.SplitExecuteUpdated(
            _batchRowsLimit > 0 ? _batchRowsLimit : 500, _batchParameterLimit > 0 ? _batchParameterLimit : 3000);

        protected override List<T1> RawExecuteUpdated()
        {
            var ret = new List<T1>();
            DbParameter[] dbParms = null;
            StringBuilder sbret = null;
            ToSqlFetch(sb =>
            {
                if (dbParms == null)
                {
                    dbParms = _params.Concat(_paramsSource).ToArray();
                    sbret = new StringBuilder();
                    sbret.Append(" RETURNING ");

                    var colidx = 0;
                    foreach (var col in _table.Columns.Values)
                    {
                        if (colidx > 0) sbret.Append(", ");
                        sbret.Append(_commonUtils.RereadColumn(col, _commonUtils.QuoteSqlName(col.Attribute.Name)))
                            .Append(" as ").Append(_commonUtils.QuoteSqlName(col.CsName));
                        ++colidx;
                    }
                }

                var sql = sb.Append(sbret).ToString();
                var before = new Aop.CurdBeforeEventArgs(_table.Type, _table, Aop.CurdType.Update, sql, dbParms);
                _orm.Aop.CurdBeforeHandler?.Invoke(this, before);

                Exception exception = null;
                try
                {
                    var rettmp = _orm.Ado.Query<T1>(_table.TypeLazy ?? _table.Type, _connection, _transaction,
                        CommandType.Text, sql, _commandTimeout, dbParms);
                    ValidateVersionAndThrow(rettmp.Count, sql, dbParms);
                    ret.AddRange(rettmp);
                }
                catch (Exception ex)
                {
                    exception = ex;
                    throw;
                }
                finally
                {
                    var after = new Aop.CurdAfterEventArgs(before, exception, ret);
                    _orm.Aop.CurdAfterHandler?.Invoke(this, after);
                }
            });
            sbret?.Clear();
            return ret;
        }

        protected override void ToSqlCase(StringBuilder caseWhen, ColumnInfo[] primarys)
        {
            if (primarys.Length == 1)
            {
                var pk = primarys.First();
                if (string.IsNullOrEmpty(InternalTableAlias) == false) caseWhen.Append(InternalTableAlias).Append(".");
                caseWhen.Append(_commonUtils.RereadColumn(pk, _commonUtils.QuoteSqlName(pk.Attribute.Name)));
                return;
            }

            caseWhen.Append("(");
            var pkidx = 0;
            foreach (var pk in primarys)
            {
                if (pkidx > 0) caseWhen.Append(" || '+' || ");
                if (string.IsNullOrEmpty(InternalTableAlias) == false) caseWhen.Append(InternalTableAlias).Append(".");
                caseWhen.Append(_commonUtils.RereadColumn(pk, _commonUtils.QuoteSqlName(pk.Attribute.Name)))
                    .Append("::text");
                ++pkidx;
            }

            caseWhen.Append(")");
        }

        protected override void ToSqlWhen(StringBuilder sb, ColumnInfo[] primarys, object d)
        {
            if (primarys.Length == 1)
            {
                sb.Append(_commonUtils.FormatSql("{0}", primarys[0].GetDbValue(d)));
                return;
            }

            sb.Append("(");
            var pkidx = 0;
            foreach (var pk in primarys)
            {
                if (pkidx > 0) sb.Append(" || '+' || ");
                sb.Append(_commonUtils.FormatSql("{0}", pk.GetDbValue(d))).Append("::text");
                ++pkidx;
            }

            sb.Append(")");
        }

        protected override void ToSqlCaseWhenEnd(StringBuilder sb, ColumnInfo col)
        {
            if (_noneParameter == false) return;
            if (col.Attribute.MapType == typeof(string))
            {
                sb.Append("::text");
                return;
            }

            var dbtype = _commonUtils.CodeFirst.GetDbInfo(col.Attribute.MapType)?.dbtype;
            if (dbtype == null) return;

            sb.Append("::").Append(dbtype);
        }

#if net40
#else
        public override Task<int> ExecuteAffrowsAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(RestAPIExtension.BaseUrl))
            {
                return base.SplitExecuteAffrowsAsync(_batchRowsLimit > 0 ? _batchRowsLimit : 500,
                    _batchParameterLimit > 0 ? _batchParameterLimit : 3000, cancellationToken);
            }
         
            return Task.FromResult(InternelExecuteAffrows());
        }
       

        public override Task<List<T1>> ExecuteUpdatedAsync(CancellationToken cancellationToken = default) =>
            base.SplitExecuteUpdatedAsync(_batchRowsLimit > 0 ? _batchRowsLimit : 500,
                _batchParameterLimit > 0 ? _batchParameterLimit : 3000, cancellationToken);

        async protected override Task<List<T1>> RawExecuteUpdatedAsync(CancellationToken cancellationToken = default)
        {
            var ret = new List<T1>();
            DbParameter[] dbParms = null;
            StringBuilder sbret = null;
            await ToSqlFetchAsync(async sb =>
            {
                if (dbParms == null)
                {
                    dbParms = _params.Concat(_paramsSource).ToArray();
                    sbret = new StringBuilder();
                    sbret.Append(" RETURNING ");

                    var colidx = 0;
                    foreach (var col in _table.Columns.Values)
                    {
                        if (colidx > 0) sbret.Append(", ");
                        sbret.Append(_commonUtils.RereadColumn(col, _commonUtils.QuoteSqlName(col.Attribute.Name)))
                            .Append(" as ").Append(_commonUtils.QuoteSqlName(col.CsName));
                        ++colidx;
                    }
                }

                var sql = sb.Append(sbret).ToString();
                var before = new Aop.CurdBeforeEventArgs(_table.Type, _table, Aop.CurdType.Update, sql, dbParms);
                _orm.Aop.CurdBeforeHandler?.Invoke(this, before);

                Exception exception = null;
                try
                {
                    var rettmp = await _orm.Ado.QueryAsync<T1>(_table.TypeLazy ?? _table.Type, _connection,
                        _transaction, CommandType.Text, sql, _commandTimeout, dbParms, cancellationToken);
                    ValidateVersionAndThrow(rettmp.Count, sql, dbParms);
                    ret.AddRange(rettmp);
                }
                catch (Exception ex)
                {
                    exception = ex;
                    throw;
                }
                finally
                {
                    var after = new Aop.CurdAfterEventArgs(before, exception, ret);
                    _orm.Aop.CurdAfterHandler?.Invoke(this, after);
                }
            });
            sbret?.Clear();
            return ret;
        }
#endif
    }
}