﻿using System.Data.SqlClient;
using OpenManta.Core;
using OpenManta.WebLib.BO;
using OpenManta.Data;

namespace OpenManta.WebLib.DAL
{
	public static class VirtualMtaTransactionDB
	{
		/// <summary>
		/// Gets a summary of a virtual MTAs transaction history.
		/// </summary>
		/// <param name="ipAddressId"></param>
		/// <returns></returns>
		public static SendTransactionSummaryCollection GetSendSummaryForIpAddress(int ipAddressId)
		{
			using (SqlConnection conn = MantaDB.GetSqlConnection())
			{
				SqlCommand cmd = conn.CreateCommand();
				cmd.CommandText = @"SELECT mta_transactionStatus_id, COUNT(*) AS 'Count'
FROM man_mta_transaction
WHERE ip_ipAddress_id = @ipAddressId
GROUP BY mta_transactionStatus_id";
				cmd.Parameters.AddWithValue("@ipAddressId", ipAddressId);
				return new SendTransactionSummaryCollection(DataRetrieval.GetCollectionFromDatabase<SendTransactionSummary>(cmd, CreateAndFillSendTransactionSummaryFromRecord));
			}
		}

		/// <summary>
		/// Creates a SendTransactionSummary from the DataRecord.
		/// </summary>
		/// <param name="record">The record of the data.</param>
		/// <returns>A filled SendTransactionSummary object.</returns>
		private static SendTransactionSummary CreateAndFillSendTransactionSummaryFromRecord(System.Data.IDataRecord record)
		{
			return new SendTransactionSummary((TransactionStatus)record.GetInt64("mta_transactionStatus_id"), record.GetInt64("count"));
		}
	}
}
