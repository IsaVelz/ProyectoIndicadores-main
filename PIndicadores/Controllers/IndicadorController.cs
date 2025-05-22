using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using PIndicadores.Models;
using PIndicadores.Services;
using System.Data.Common;
using System.Text.Json;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace PIndicadores.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class IndicadorController : Controller
    {
        private readonly ControlConexion controlConexion; // Declara una instancia del servicio ControlConexion.
        private readonly IConfiguration _configuration; // Declara una instancia de la configuración de la aplicación.

        public IndicadorController(ControlConexion controlConexion, IConfiguration configuration)
        {
            this.controlConexion = controlConexion ?? throw new ArgumentNullException(nameof(controlConexion));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        [HttpPost]
        [Authorize(Roles = "admin")] //Solo el administrador puede crear nuevas entidades
        public IActionResult GuardarIndicadores([FromBody] GuardarIndicador guardarIndicador)
        {
            if (guardarIndicador == null || guardarIndicador.Indicador == null)
                return BadRequest("El objeto indicador es obligatorio.");

            try
            {
                // 1. Convertir Indicadores a diccionario
                var datosIndicador = IndicadorAObjeto(guardarIndicador.Indicador);

                // 2. Crear Indicador (y obtener el ID generado)
                var idGenerado = CrearYObtenerId("Indicador", datosIndicador);
                if (idGenerado == null)
                    return StatusCode(500, "No se pudo crear el indicador.");

                // 3. Insertar las tablas relacionadas
                GuardarHijos("variablesporindicador", guardarIndicador.variablesporindicador, idGenerado.Value);
                GuardarHijos("responsablesporindicador", guardarIndicador.responsablesporindicador, idGenerado.Value);
                GuardarHijos("represenvisualporindicador", guardarIndicador.represenvisualporindicador, idGenerado.Value);
                GuardarHijos("resultadoindicador", guardarIndicador.resultadoindicador, idGenerado.Value);

                return Ok(new { mensaje = "Indicador y sus relaciones guardados correctamente", idIndicador = idGenerado });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al guardar: {ex.Message}");
            }
        }

        private int? CrearYObtenerId(string tabla, Dictionary<string, object?> datos)
        {
            try
            {
                // Convierte cualquier JsonElement a su tipo nativo correspondiente
                var datosConvertidos = datos
                    .Where(kvp => !string.Equals(kvp.Key, "id", StringComparison.OrdinalIgnoreCase)) // O reemplaza con el nombre real de tu PK
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value is JsonElement je ? ConvertirJsonElement(je) : kvp.Value
                    );

                // Construcción de columnas y parámetros
                string columnas = string.Join(",", datosConvertidos.Keys);
                string valores = string.Join(",", datosConvertidos.Keys.Select(k => "@" + k));
                string consultaSQL = $"INSERT INTO {tabla} ({columnas}) VALUES ({valores}); SELECT SCOPE_IDENTITY();";

                // Crear los parámetros
                var parametros = datosConvertidos.Select(p => CrearParametro("@" + p.Key, p.Value)).ToArray();

                // Ejecutar consulta
                controlConexion.AbrirBd();
                var resultado = controlConexion.EjecutarEscalar(consultaSQL, parametros);
                controlConexion.CerrarBd();

                return Convert.ToInt32(resultado);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error al insertar en " + tabla + ": " + ex.Message);
                foreach (var kv in datos)
                {
                    Console.WriteLine($"Clave: {kv.Key}, Valor: {kv.Value}, Tipo: {kv.Value?.GetType().Name}");
                }
                return null;
            }
        }

        private object? ConvertirJsonElement(JsonElement elementoJson)
        {
            switch (elementoJson.ValueKind)
            {
                case JsonValueKind.Null:
                    return null;
                case JsonValueKind.String:
                    string valorStr = elementoJson.GetString()!;
                    if (DateTime.TryParse(valorStr, out var fecha))
                        return fecha;
                    return valorStr;
                case JsonValueKind.Number:
                    if (elementoJson.TryGetInt64(out var valInt))
                        return valInt;
                    if (elementoJson.TryGetDecimal(out var valDec))
                        return valDec;
                    return elementoJson.GetDouble();
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return elementoJson.GetBoolean();
                case JsonValueKind.Array:
                case JsonValueKind.Object:
                    return elementoJson.GetRawText(); // Guarda como JSON string
                default:
                    return elementoJson.ToString();
            }
        }

        private void GuardarHijos<T>(string tabla, List<T> lista, int idIndicador)
        {
            if (lista == null || lista.Count == 0)
                return;

            foreach (var item in lista)
            {
                var datos = EntidadAObjeto(item);
                datos["fkidindicador"] = idIndicador; // Establece la clave foránea
                Crear(tabla, datos); // Puedes usar tu método existente aquí
            }
        }

        private void Crear(string nombreTabla, Dictionary<string, object?> datosEntidad)
        {
            var datosConvertidos = datosEntidad
              .Where(kvp => !string.Equals(kvp.Key, "id", StringComparison.OrdinalIgnoreCase)) // O reemplaza con el nombre real de tu PK
              .ToDictionary(
                  kvp => kvp.Key,
                  kvp => kvp.Value is JsonElement je ? ConvertirJsonElement(je) : kvp.Value
              );

            var columnas = string.Join(",", datosConvertidos.Keys);
            var valores = string.Join(",", datosConvertidos.Keys.Select(k => "@" + k));
            var consultaSQL = $"INSERT INTO {nombreTabla} ({columnas}) VALUES ({valores})";

            var parametros = datosConvertidos
                .Select(p => CrearParametro("@" + p.Key, p.Value))
                .ToArray();

            controlConexion.AbrirBd();
            controlConexion.EjecutarEscalar(consultaSQL, parametros);
            controlConexion.CerrarBd();
        }

        private object? ConvertirSiJsonElement(object? valor)
        {
            if (valor is JsonElement je)
                return ConvertirJsonElement(je);
            return valor;
        }


        private DbParameter CrearParametro(string nombre, object? valor)
        {
            return new SqlParameter(nombre, valor ?? DBNull.Value); // Crea un parámetro para SQL Server y LocalDB.
        }

        private Dictionary<string, object?> IndicadorAObjeto(Indicadores entidad)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(entidad);
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
        }

        private Dictionary<string, object?> EntidadAObjeto<T>(T entidad)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(entidad);
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
        }
    }
}
