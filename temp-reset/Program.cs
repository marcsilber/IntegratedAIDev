using Microsoft.Data.Sqlite;

var dbPath = @"c:\Users\MarcSilberbauer\source\repos\AIDev\src\AIDev.Api\AIDev.Api\aidev.db";
using var connection = new SqliteConnection($"Data Source={dbPath}");
connection.Open();

// Read latest agent review for request 3
var cmd = connection.CreateCommand();
cmd.CommandText = "SELECT Decision, Reasoning, AlignmentScore, CompletenessScore, SalesAlignmentScore, Tags FROM AgentReviews WHERE DevRequestId = 3 ORDER BY Id DESC LIMIT 1";
using var reader = cmd.ExecuteReader();
if (reader.Read())
{
    Console.WriteLine($"Decision: {reader.GetString(0)}");
    Console.WriteLine($"Alignment: {reader.GetInt32(2)}, Completeness: {reader.GetInt32(3)}, SalesAlignment: {reader.GetInt32(4)}");
    Console.WriteLine($"Tags: {(reader.IsDBNull(5) ? "NULL" : reader.GetString(5))}");
    Console.WriteLine($"---REASONING---");
    Console.WriteLine(reader.GetString(1));
}

// Also read the comment
reader.Close();
cmd.CommandText = "SELECT Content FROM RequestComments WHERE DevRequestId = 3 AND IsAgentComment = 1 ORDER BY Id DESC LIMIT 1";
using var reader2 = cmd.ExecuteReader();
if (reader2.Read())
{
    Console.WriteLine($"\n---AGENT COMMENT---");
    Console.WriteLine(reader2.GetString(0));
}
