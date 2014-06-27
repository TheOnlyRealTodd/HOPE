﻿using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Clifton.Assertions;
using Clifton.ExtensionMethods;
using Clifton.Receptor.Interfaces;
using Clifton.SemanticTypeSystem.Interfaces;
using Clifton.Tools.Data;
using Clifton.Tools.Strings.Extensions;

/*
 * Renaming columns in SQLite:
 * begin;
 * PRAGMA writable_schema=1;
 * UPDATE sqlite_master Set SQL=REPLACE(SQL, 'ImageFile', 'ImageFilename') where name = 'APOD';
 * PRAGMA writable_schema=0;
 * commit;
 */

namespace PersistenceReceptor
{
	public class ReceptorDefinition : BaseReceptor
	{
		public override string Name { get { return "SQLite Persistor"; } }
		public override bool IsEdgeReceptor { get { return true; } }
		protected SQLiteConnection conn;
		protected Dictionary<string, Action<dynamic>> protocolActionMap;
		protected Dictionary<string, Action<dynamic>> crudMap;
		const string DatabaseFileName = "hope.db";

		public ReceptorDefinition(IReceptorSystem rsys) : base(rsys)
		{
			protocolActionMap = new Dictionary<string, Action<dynamic>>();
			protocolActionMap["RequireTable"] = new Action<dynamic>((s) => RequireTable(s));
			protocolActionMap["RequireView"] = new Action<dynamic>((s) => RequireView(s));
			protocolActionMap["DropView"] = new Action<dynamic>((s) => DropView(s));
			protocolActionMap["DatabaseRecord"] = new Action<dynamic>((s) => DatabaseRecord(s));

			crudMap = new Dictionary<string, Action<dynamic>>();
			crudMap["insert"] = new Action<dynamic>((s) => Insert(s));
			crudMap["insertifmissing"] = new Action<dynamic>((s) => InsertIfMissing(s));
			crudMap["getid"] = new Action<dynamic>((s) => GetID(s));
			crudMap["update"] = new Action<dynamic>((s) => Update(s));
			crudMap["delete"] = new Action<dynamic>((s) => Delete(s));
			crudMap["select"] = new Action<dynamic>((s) => Select(s));

			protocolActionMap.Keys.ForEach(k => AddReceiveProtocol(k));
			rsys.GetProtocolsEndingWith("Recordset").ForEach(p => AddEmitProtocol(p));
			AddEmitProtocol("IDReturn");

			CreateDBIfMissing();
			OpenDB();
		}

		public override void Terminate()
		{
			try
			{
				conn.Close();
				conn.Dispose();
			}
			catch
			{
			}
			finally
			{
				// As per this post:
				// http://stackoverflow.com/questions/12532729/sqlite-keeps-the-database-locked-even-after-the-connection-is-closed
				// GC.Collect() is required to ensure that the file handle is released NOW (not when the GC gets a round tuit.  ;)
				GC.Collect();
			}
		}

		public override void ProcessCarrier(ICarrier carrier)
		{
			protocolActionMap[carrier.Protocol.DeclTypeName](carrier.Signal);
		}

		/// <summary>
		/// Create the database if it doesn't exist.
		/// </summary>
		protected void CreateDBIfMissing()
		{
			string subPath = Path.GetDirectoryName(DatabaseFileName);
			
			if (!File.Exists(DatabaseFileName))
			{
				SQLiteConnection.CreateFile(DatabaseFileName);
			}
		}

		protected void OpenDB()
		{
			conn = new SQLiteConnection("Data Source = " + DatabaseFileName); 
			conn.Open();
		}

		protected void RequireTable(dynamic signal)
		{
			if (!TableExists(signal.TableName))
			{
				StringBuilder sb = new StringBuilder("create table " + signal.TableName + "(");
				string schema = signal.Schema;

				// Always create a primary key as the field ID. 
				// There is no need to put this into the semantic type definition unless it's required for queries.
				sb.Append("ID INTEGER PRIMARY KEY AUTOINCREMENT");
				List<INativeType> ntypes = rsys.SemanticTypeSystem.GetSemanticTypeStruct(schema).NativeTypes;
				List<ISemanticElement> stypes = rsys.SemanticTypeSystem.GetSemanticTypeStruct(schema).SemanticElements;
				
				// Ignore ID field in the schema, as we specifically create it above.
				// we ignore types, as per the SQLite 3 documentation:
				// "Any column in an SQLite version 3 database, except an INTEGER PRIMARY KEY column, may be used to store a value of any storage class."
				// http://www.sqlite.org/datatype3.html

				var nNames = ntypes.Where(t=>t.Name.ToLower() != "id").Select(t => t.Name);

				// The root semantic type name is the name of the field.
				var sNames = stypes.Where(t => t.Element.Struct.DeclTypeName.ToLower() != "id").Select(t => t.Element.Struct.DeclTypeName);

				var names = nNames.Concat(sNames);

				// We theoretically could have a schema that defines only the ID field.
				if (names.Count() > 0)
				{
					sb.Append(", ");
				}

				string fields = String.Join(", ", names);
				sb.Append(fields);
				sb.Append(");");

				Execute(sb.ToString());
			}
		}

		/// <summary>
		/// Creates a view given the supplied SQL statement and view name if the view does not exist.
		/// Does not replace the view.  Use DropView to first drop an existing view.
		/// </summary>
		protected void RequireView(dynamic signal)
		{
			// Execute("drop view " + signal.ViewName);
			StringBuilder sb = new StringBuilder("create view if not exists ");
			sb.Append(signal.ViewName);
			sb.Append(" as ");
			sb.Append(signal.Sql);
			Execute(sb.ToString());
		}

		protected void DropView(dynamic signal)
		{
			Execute("drop view if exists " + signal.ViewName);
		}

		protected void DatabaseRecord(dynamic signal)
		{
			crudMap[signal.Action.ToLower()](signal);
		}

		protected void Insert(dynamic signal)
		{
			Dictionary<string, object> cvMap = GetColumnValueMap(signal.Row);
			StringBuilder sb = new StringBuilder("insert into " + signal.TableName + "(");
			sb.Append(String.Join(", ", (from c in cvMap where c.Value != null select c.Key).ToArray()));
			sb.Append(") values (");
			sb.Append(String.Join(",", (from c in cvMap where c.Value != null select "@" + c.Key).ToArray()));
			sb.Append(");");

			SQLiteCommand cmd = conn.CreateCommand();
			(from c in cvMap where c.Value != null select c).ForEach(kvp => cmd.Parameters.Add(new SQLiteParameter("@" + kvp.Key, kvp.Value)));
			cmd.CommandText = sb.ToString();
			cmd.ExecuteNonQuery();

			cmd.CommandText = "SELECT last_insert_rowid()";
			int id = Convert.ToInt32(cmd.ExecuteScalar());

			EmitID(id, signal.TableName, signal.Tag, true);
 
			cmd.Dispose();
		}

		/// <summary>
		/// Check for the existence of a row with the unique key.
		/// If it doesn't exist, insert the entire record.
		/// </summary>
		protected void InsertIfMissing(dynamic signal)
		{
			Dictionary<string, object> cvMap = GetColumnValueMap(signal.Row);
			StringBuilder sb = new StringBuilder("select ID from ");
			sb.Append(signal.TableName);
			sb.Append(" where ");

			// Time to deal with composite keys.
			string uks = signal.UniqueKey;
			string and = String.Empty;
			var ukitems = uks.Split(',').Select(s => s.Trim());

			ukitems.ForEach(uk =>
			{
				sb.Append(and);
				and = " and ";
				sb.Append(uk);
				sb.Append(" = ");
				var val = cvMap[uk];

				// Stoopid SQLite treats integer types and string types differently in where clauses.
				// TODO: I suppose the value should be put into a parameter, and then I'm not seen
				// as stoopid for being open to SQL injection attacks.  And yes, I know how to spell stupid.
				if (val.GetType() == typeof(string))
				{
					string str = val.ToString();
					str = str.Replace("'", "''");			// Any single quotes need to be replaced with double-quotes.  TODO: Wouldn't be an issue if we used parameters.
					sb.Append(str.SingleQuote());
				}
				else
				{
					sb.Append(val);
				}
			});

			SQLiteCommand cmd = conn.CreateCommand();
			cmd.CommandText = sb.ToString();
			object id = null;

			// TODO: Why doesn't this return DBNull.Value?
			id = cmd.ExecuteScalar();

			if (id == null)
			{
				Insert(signal);
			}
			else
			{
				EmitID(Convert.ToInt32(id), signal.TableName, signal.Tag, false);
			}
		}

		protected void GetID(dynamic signal)
		{
			StringBuilder sb = new StringBuilder("select ID from ");
			sb.Append(signal.TableName);
			sb.Append(" where ");
			sb.Append(signal.UniqueKey);
			sb.Append(" = ");
			string ukValue = signal.UniqueKeyValue;
			int outVal;

			// TODO: more kludges.  If the value is strictly a number, then don't surround with quotes.
			// Read the comment regarding type comparison in InsertIfMissing above.
			if (Int32.TryParse(ukValue, out outVal))
			{
				sb.Append(ukValue);
			}
			else
			{
				sb.Append(ukValue.SingleQuote());
			}

			SQLiteCommand cmd = conn.CreateCommand();
			cmd.CommandText = sb.ToString();

			// TODO: Why doesn't this return DBNull.Value?
			object id = cmd.ExecuteScalar();

			if (id == null)
			{
				// TODO: Oh boy, this is really kludgy.  We should be able to signal "no records found."
				id = -1;
			}

			int iid = Convert.ToInt32(id);

			EmitID(iid, signal.TableName, signal.Tag, iid == -1);
		}

		protected void Update(dynamic signal)
		{
			Dictionary<string, object> cvMap = GetColumnValueMap(signal.Row);
			StringBuilder sb = new StringBuilder("update " + signal.TableName + " set ");
			sb.Append(String.Join(",", (from c in cvMap where c.Value != null select c.Key + "= @" + c.Key).ToArray()));
			sb.Append(" where " + signal.Where);		// where is required.

			SQLiteCommand cmd = conn.CreateCommand();
			(from c in cvMap where c.Value != null select c).ForEach(kvp => cmd.Parameters.Add(new SQLiteParameter("@" + kvp.Key, kvp.Value)));
			cmd.CommandText = sb.ToString();
			cmd.ExecuteNonQuery();
			cmd.Dispose();
		}

		protected void Delete(dynamic signal)
		{
			// Where clause is optional.
			string sql = "delete from " + signal.TableName;
			if (signal.Where != null) sql = sql + " where " + signal.Where;
			SQLiteCommand cmd = conn.CreateCommand();
			cmd.CommandText = sql;
			cmd.ExecuteNonQuery();
			cmd.Dispose();
		}

		protected void Select(dynamic signal)
		{
			string schema = signal.ResponseProtocol;
			Assert.That(schema != null, "Response protocol must be provided.");			
			StringBuilder sb = new StringBuilder("select ");
			List<IGetSetSemanticType> types = rsys.SemanticTypeSystem.GetSemanticTypeStruct(schema).AllTypes;

			// A view could be queried using the table name field, but this makes a clear distinction that we will probably use 
			// later when we incorporate more if Interacx into the persistence receptor.
			string tableOrViewName = (signal.TableName != null ? signal.TableName : signal.ViewName);

			// TODO: Join these through the common interface IGetSetSemanticType

			sb.Append(String.Join(", ", (from c in types select c.Name).ToArray()));
			sb.Append(" from " + tableOrViewName);

			string where = signal.Where;

			if (where != null)
			{
				where = where.Replace("'", "''");			// TODO: Use parameters?
				sb.Append(" where " + where);
			}

			// support for group by is sort of pointless since we're not supporting any mechanism for aggregate functions.
			if (signal.GroupBy != null) sb.Append(" group by " + signal.GroupBy);
			if (signal.OrderBy != null) sb.Append(" order by " + signal.OrderBy);

			SQLiteCommand cmd = conn.CreateCommand();
			cmd.CommandText = sb.ToString();
            SQLiteDataReader reader = cmd.ExecuteReader();

			// Create an instance of the recordset type.
			ISemanticTypeStruct collectionProtocol = rsys.SemanticTypeSystem.GetSemanticTypeStruct(signal.ResponseProtocol + "Recordset");
			dynamic collection = rsys.SemanticTypeSystem.Create(signal.ResponseProtocol + "Recordset");
			collection.Recordset = new List<dynamic>();
			// Return whatever we were sent, so caller can have a reference that it needs.
			collection.Tag = signal.Tag;			

			while (reader.Read())
			{
				ISemanticTypeStruct protocol = rsys.SemanticTypeSystem.GetSemanticTypeStruct(signal.ResponseProtocol);
				dynamic outSignal = rsys.SemanticTypeSystem.Create(signal.ResponseProtocol);
				types.ForEach(t=>t.SetValue(rsys.SemanticTypeSystem, outSignal, reader[t.Name]));

				// Populate the output signal with the fields retrieved from the query, as specified by the requested response protocol
				// ntypes.Cast<IGetSetSemanticType>().ForEach(t => t.SetValue(rsys.SemanticTypeSystem, outSignal, reader[t.Name]));

				// A semantic type is a different beast, potentially with child ST's. 
				// We need to get to the native type parameter to properly initialize this type.
				// Delegate this whole issue to the semantic type itself.
				// An important thing -- schema semantic types can only have one property per type, otherwise we won't know which
				// property to set.
				// stypes.ForEach(t => t.SetValue(rsys.SemanticTypeSystem, outSignal, reader[t.Element.Struct.DeclTypeName]));

				// Add the record to the recordset.
				collection.Recordset.Add(outSignal);

				// rsys.CreateCarrier(this, protocol, outSignal);
			}

			cmd.Dispose();

			// Create the carrier for the recordset.
			rsys.CreateCarrier(this, collectionProtocol, collection);
		}

		protected Dictionary<string, object> GetColumnValueMap(ICarrier carrier)
		{
			// List<INativeType> types = rsys.SemanticTypeSystem.GetSemanticTypeStruct(carrier.Protocol.DeclTypeName).NativeTypes;

			List<IGetSetSemanticType> types = rsys.SemanticTypeSystem.GetSemanticTypeStruct(carrier.Protocol.DeclTypeName).AllTypes;
			Dictionary<string, object> cvMap = new Dictionary<string, object>();
			types.ForEach(t => cvMap[t.Name] = t.GetValue(rsys.SemanticTypeSystem, carrier.Signal));

			return cvMap;
		}

		protected bool TableExists(string tableName)
		{
			string sql = "SELECT name FROM sqlite_master WHERE type='table' AND name=" + tableName.SingleQuote() + ";";
			string name = QueryScalar<string>(sql);

			return tableName == name;
		}

		protected T QueryScalar<T>(string query)
		{
			SQLiteCommand cmd = conn.CreateCommand();
			cmd.CommandText = query;
			T result = (T)cmd.ExecuteScalar();
			cmd.Dispose();

			return result;
		}

		protected void Execute(string sql)
		{
			SQLiteCommand cmd = conn.CreateCommand();
			cmd.CommandText = sql;
			cmd.ExecuteNonQuery();
			cmd.Dispose();
		}

		/// <summary>
		/// Emits the ID along with the associated table name, the querent's tag, and a flag indicating
		/// whether this is a new row or an existing row.  
		/// "Insert" always returns true for NewRow.
		/// "InsertIfMissing" returns true if an insert occurs, otherwise false.
		/// "GetID" return true if the record was found using the UK, otherwise false.
		/// </summary>
		protected void EmitID(int id, string tableName, string tag, bool newRow)
		{
			// Respond with the ID value.
			CreateCarrier("IDReturn", idSignal =>
			{
				idSignal.ID = id;
				idSignal.TableName = tableName;
				idSignal.NewRow = newRow;
				idSignal.Tag = tag;
			});
		}
	}
}


