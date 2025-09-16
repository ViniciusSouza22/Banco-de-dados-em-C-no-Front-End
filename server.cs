using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors();

var app = builder.Build();

app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
app.UseRouting();
app.UseStaticFiles();

// Sua string de conexão. Se for necessário, ajuste a porta e credenciais.
string connectionString = "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=vini1010";

// Endpoint para receber e inserir dados na tabela 'vendas'
app.MapPost("/enviar-dados", async (HttpContext context) =>
{
    try
    {
        var dadosVenda = await JsonSerializer.DeserializeAsync<DadosVendaRequest>(
            context.Request.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (dadosVenda == null)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Dados de venda inválidos.");
            return;
        }

        Console.WriteLine("Dados de venda recebidos:");
        Console.WriteLine($"Produto: {dadosVenda.Produto}, Cliente: {dadosVenda.Cliente}, Preco: {dadosVenda.Preco}, Vendas: {dadosVenda.Vendas}, Quantidade: {dadosVenda.Quantidade}, Data: {dadosVenda.Data}");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var query = @"
            INSERT INTO vendas (produto, cliente, preco, vendas, quantidade, data)
            VALUES (@produto, @cliente, @preco, @vendas, @quantidade, @data)";

        await using var cmd = new NpgsqlCommand(query, connection);
        cmd.Parameters.AddWithValue("produto", dadosVenda.Produto);
        cmd.Parameters.AddWithValue("cliente", dadosVenda.Cliente);
        cmd.Parameters.AddWithValue("preco", dadosVenda.Preco);
        cmd.Parameters.AddWithValue("vendas", dadosVenda.Vendas);
        cmd.Parameters.AddWithValue("quantidade", dadosVenda.Quantidade);
        cmd.Parameters.AddWithValue("data", dadosVenda.Data);

        await cmd.ExecuteNonQueryAsync();

        Console.WriteLine("Dados de venda inseridos com sucesso!");
        context.Response.StatusCode = 200;
        await context.Response.WriteAsync("Dados inseridos com sucesso.");
    }
    catch (NpgsqlException nex)
    {
        Console.WriteLine($"Erro no banco de dados (Npgsql): {nex.Message}");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync($"Erro no banco de dados: {nex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro no servidor: {ex}");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync($"Erro no servidor: {ex.Message}");
    }
});


// Endpoint para filtrar dados da tabela 'vendas'
app.MapPost("/filtrar-dados", async (HttpContext context) =>
{
    try
    {
        var filtro = await JsonSerializer.DeserializeAsync<FiltroRequest>(
            context.Request.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        Console.WriteLine($"Filtro recebido -> Nome: '{filtro?.Nome}', Cliente: '{filtro?.Cliente}', Data: '{filtro?.Data}'");

        var results = new List<Dictionary<string, object>>();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var (query, parameters) = BuildQuery(filtro);

        Console.WriteLine("Query executada:");
        Console.WriteLine(query);
        foreach (var p in parameters)
            Console.WriteLine($"{p.ParameterName} = {p.Value}");

        await using var cmd = new NpgsqlCommand(query, connection);
        cmd.Parameters.AddRange(parameters.ToArray());

        await using var dbReader = await cmd.ExecuteReaderAsync();

        while (await dbReader.ReadAsync())
        {
            var row = new Dictionary<string, object>();
            for (int i = 0; i < dbReader.FieldCount; i++)
            {
                row[dbReader.GetName(i).ToLower()] = dbReader.GetValue(i);
            }
            results.Add(row);
        }

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(results));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro no servidor: {ex}");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync($"Erro no servidor: {ex.Message}");
    }
});

// Endpoint para filtrar dados da tabela 'contabilidade'
app.MapPost("/filtrar-contabilidade", async (HttpContext context) =>
{
    try
    {
        var filtro = await JsonSerializer.DeserializeAsync<FiltroContabilidadeRequest>(
            context.Request.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        Console.WriteLine("Requisição POST recebida em /filtrar-contabilidade");

        if (filtro == null)
        {
            Console.WriteLine("Erro: Dados do filtro não foram desserializados corretamente.");
            context.Response.StatusCode = 400; // Bad Request
            await context.Response.WriteAsync("Dados de filtro inválidos.");
            return;
        }

        Console.WriteLine($"Filtro de contabilidade recebido -> Mês: '{filtro.Mes}', Ano: '{filtro.Ano}'");

        var results = new List<Dictionary<string, object>>();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        Console.WriteLine("Conexão com o banco de dados aberta com sucesso.");

        var (query, parameters) = BuildContabilidadeQuery(filtro);

        Console.WriteLine("Query de contabilidade executada:");
        Console.WriteLine(query);
        foreach (var p in parameters)
            Console.WriteLine($"{p.ParameterName} = {p.Value}");

        await using var cmd = new NpgsqlCommand(query, connection);
        cmd.Parameters.AddRange(parameters.ToArray());

        await using var dbReader = await cmd.ExecuteReaderAsync();
        Console.WriteLine("Leitor de dados executado.");

        if (!dbReader.HasRows)
        {
            Console.WriteLine("Nenhuma linha encontrada no banco de dados para os filtros especificados.");
        }

        while (await dbReader.ReadAsync())
        {
            var row = new Dictionary<string, object>();
            for (int i = 0; i < dbReader.FieldCount; i++)
            {
                row[dbReader.GetName(i).ToLower()] = dbReader.GetValue(i);
            }
            results.Add(row);
        }
        Console.WriteLine($"Total de {results.Count} registro(s) encontrado(s).");

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(results));
    }
    catch (NpgsqlException nex)
    {
        Console.WriteLine($"Erro no banco de dados (Npgsql): {nex.Message}");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync($"Erro no banco de dados: {nex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro geral no servidor (contabilidade): {ex}");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync($"Erro no servidor: {ex.Message}");
    }
});

// Endpoint para receber e inserir dados na tabela 'contabilidade'
app.MapPost("/enviar-contabilidade", async (HttpContext context) =>
{
    // Habilitar buffer para ler o corpo da requisição múltiplas vezes
    context.Request.EnableBuffering();
    
    // Ler corpo da requisição para logging
    var bodyReader = new StreamReader(context.Request.Body);
    var bodyContent = await bodyReader.ReadToEndAsync();
    Console.WriteLine($"Corpo da requisição recebido: {bodyContent}");
    
    // Resetar a posição do stream para o início
    context.Request.Body.Position = 0;

    try
    {
        var options = new JsonSerializerOptions 
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            Converters = { new DecimalConverter() }
        };
        
        var dadosContabilidade = await JsonSerializer.DeserializeAsync<DadosContabilidadeRequest>(
            context.Request.Body,
            options
        );

        if (dadosContabilidade == null)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Dados contábeis inválidos.");
            return;
        }

        Console.WriteLine("Dados de contabilidade desserializados:");
        Console.WriteLine($"Impostos: {dadosContabilidade.Impostos}");
        Console.WriteLine($"Matéria-Prima: {dadosContabilidade.MateriaPrima}");
        Console.WriteLine($"Apurado do Mês: {dadosContabilidade.ApuradoMes}");
        Console.WriteLine($"Lucro do Mês: {dadosContabilidade.LucroMes}");
        Console.WriteLine($"Salários: {dadosContabilidade.Salarios}");
        Console.WriteLine($"Total de Receitas: {dadosContabilidade.TotalReceitas}");
        Console.WriteLine($"Data do Mês: {dadosContabilidade.DataMes}");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var query = @"
            INSERT INTO contabilidade (impostos, materia_prima, apurado_mes, lucro_mes, salarios, total_receitas, data_mes)
            VALUES (@impostos, @materia_prima, @apurado_mes, @lucro_mes, @salarios, @total_receitas, @data_mes)";

        await using var cmd = new NpgsqlCommand(query, connection);
        cmd.Parameters.AddWithValue("impostos", dadosContabilidade.Impostos);
        cmd.Parameters.AddWithValue("materia_prima", dadosContabilidade.MateriaPrima);
        cmd.Parameters.AddWithValue("apurado_mes", dadosContabilidade.ApuradoMes);
        cmd.Parameters.AddWithValue("lucro_mes", dadosContabilidade.LucroMes);
        cmd.Parameters.AddWithValue("salarios", dadosContabilidade.Salarios);
        cmd.Parameters.AddWithValue("total_receitas", dadosContabilidade.TotalReceitas);
        cmd.Parameters.AddWithValue("data_mes", dadosContabilidade.DataMes);

        await cmd.ExecuteNonQueryAsync();

        Console.WriteLine("Dados de contabilidade inseridos com sucesso!");
        context.Response.StatusCode = 200;
        await context.Response.WriteAsync("Dados de contabilidade inseridos com sucesso.");
    }
    catch (NpgsqlException nex)
    {
        Console.WriteLine($"Erro no banco de dados (Npgsql): {nex.Message}");
        Console.WriteLine($"Detalhes: {nex.InnerException?.Message}");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync($"Erro no banco de dados: {nex.Message}");
    }
    catch (JsonException jex)
    {
        Console.WriteLine($"Erro na desserialização JSON: {jex.Message}");
        Console.WriteLine($"Path: {jex.Path}, Line: {jex.LineNumber}");
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync($"Erro no formato dos dados: {jex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro no servidor: {ex}");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync($"Erro no servidor: {ex.Message}");
    }
});


app.Run("http://localhost:3001");

// Função para construir a query da tabela 'vendas'
static (string, List<NpgsqlParameter>) BuildQuery(FiltroRequest? filtro)
{
    var conditions = new List<string>();
    var parameters = new List<NpgsqlParameter>();

    if (!string.IsNullOrWhiteSpace(filtro?.Nome))
    {
        conditions.Add("COALESCE(produto, '') ILIKE @nome");
        parameters.Add(new NpgsqlParameter("@nome", $"%{filtro.Nome.Trim()}%"));
    }

    if (!string.IsNullOrWhiteSpace(filtro?.Cliente))
    {
        conditions.Add("COALESCE(cliente, '') ILIKE @cliente");
        parameters.Add(new NpgsqlParameter("@cliente", $"%{filtro.Cliente.Trim()}%"));
    }

    if (!string.IsNullOrWhiteSpace(filtro?.Data))
    {
        if (DateTime.TryParse(filtro.Data, out var data))
        {
            conditions.Add("CAST(data AS DATE) = @data");
            parameters.Add(new NpgsqlParameter("@data", data.Date));
        }
    }

    var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
    var query = $"SELECT * FROM vendas {whereClause} ORDER BY data DESC";

    return (query, parameters);
}

// Função para construir a query da tabela 'contabilidade'
static (string, List<NpgsqlParameter>) BuildContabilidadeQuery(FiltroContabilidadeRequest? filtro)
{
    var conditions = new List<string>();
    var parameters = new List<NpgsqlParameter>();

    if (filtro != null && filtro.Ano.HasValue && filtro.Mes.HasValue)
    {
        conditions.Add("EXTRACT(YEAR FROM data_mes) = @ano");
        conditions.Add("EXTRACT(MONTH FROM data_mes) = @mes");
        parameters.Add(new NpgsqlParameter("@ano", filtro.Ano.Value));
        parameters.Add(new NpgsqlParameter("@mes", filtro.Mes.Value));
    }

    var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
    var query = $"SELECT * FROM contabilidade {whereClause} ORDER BY data_mes DESC";

    return (query, parameters);
}

// Conversor personalizado para decimal
public class DecimalConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetDecimal();
        }
        if (reader.TokenType == JsonTokenType.String)
        {
            var stringValue = reader.GetString();
            if (decimal.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }
        return 0;
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}

public class FiltroRequest
{
    public string? Nome { get; set; }
    public string? Cliente { get; set; }
    public string? Data { get; set; }
}

public class FiltroContabilidadeRequest
{
    public int? Mes { get; set; }
    public int? Ano { get; set; }
}

public class DadosVendaRequest
{
    public string? Produto { get; set; }
    public string? Cliente { get; set; }
    public decimal Preco { get; set; }
    public decimal Vendas { get; set; }
    public int Quantidade { get; set; }
    public DateTime Data { get; set; }
}

public class DadosContabilidadeRequest
{
    [JsonPropertyName("impostos")]
    public decimal Impostos { get; set; }

    [JsonPropertyName("materia_prima")]
    public decimal MateriaPrima { get; set; }

    [JsonPropertyName("apurado_mes")]
    public decimal ApuradoMes { get; set; }

    [JsonPropertyName("lucro_mes")]
    public decimal LucroMes { get; set; }

    [JsonPropertyName("salarios")]
    public decimal Salarios { get; set; }

    [JsonPropertyName("total_receitas")]
    public decimal TotalReceitas { get; set; }

    [JsonPropertyName("data_mes")]
    [JsonConverter(typeof(DateFormatConverter))]
    public DateTime DataMes { get; set; }
}

// Conversor personalizado para datas
public class DateFormatConverter : JsonConverter<DateTime>
{
    private const string DateFormat = "yyyy-MM-dd";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return DateTime.ParseExact(reader.GetString()!, DateFormat, CultureInfo.InvariantCulture);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(DateFormat));
    }
}