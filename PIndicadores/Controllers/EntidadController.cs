#nullable enable // Habilita las características de referencia nula en C#, permitiendo anotaciones y advertencias relacionadas con posibles valores nulos.
using System; // Importa el espacio de nombres que contiene tipos fundamentales como Exception, Console, etc.
using System.Collections.Generic; // Importa el espacio de nombres para colecciones genéricas como Dictionary.
using System.Data; // Importa el espacio de nombres para clases relacionadas con bases de datos.
using System.Data.Common; // Importa el espacio de nombres que define la clase base para proveedores de datos.
using Microsoft.AspNetCore.Authorization; // Importa el espacio de nombres para el control de autorización en ASP.NET Core.
using Microsoft.AspNetCore.Mvc; // Importa el espacio de nombres para la creación de controladores en ASP.NET Core.
using Microsoft.Extensions.Configuration; // Importa el espacio de nombres para acceder a la configuración de la aplicación.
using Microsoft.Data.SqlClient; // Importa el espacio de nombres necesario para trabajar con SQL Server y LocalDB.
using System.Linq; // Importa el espacio de nombres para operaciones de consulta con LINQ.
using System.Text.Json; // Importa el espacio de nombres para manejar JSON.
using PIndicadores.Models; // Importa los modelos del proyecto.
using PIndicadores.Services; // Importa los servicios del proyecto.
using BCrypt.Net; // Importa el espacio de nombres para trabajar con BCrypt para hashing de contraseñas.

namespace PIndicadores.Controllers
{
    [Route("api/{nombreProyecto}/{nombreTabla}")] // Define la ruta de la API para este controlador.
    [ApiController] // Indica que esta clase es un controlador de API.
    [Authorize] // Requiere autorización para acceder a los métodos de este controlador.
    public class EntidadesController : ControllerBase // Define un controlador llamado `EntidadesController`.
    {
        private readonly ControlConexion controlConexion; // Declara una instancia del servicio ControlConexion.
        private readonly IConfiguration _configuration; // Declara una instancia de la configuración de la aplicación.

        // Constructor que recibe las dependencias necesarias y lanza excepciones si son nulas.
        public EntidadesController(ControlConexion controlConexion, IConfiguration configuration)
        {
            this.controlConexion = controlConexion ?? throw new ArgumentNullException(nameof(controlConexion));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        // Indica que este método se ejecuta cuando se hace una petición HTTP GET.
        [HttpGet]
        [Authorize(Roles = "admin,Verificador,Validador")] //Define los roles tiene acceso al ENDPOINT
                                                           // Acción que lista registros de una tabla, con posibilidad de incluir descripciones de llaves foráneas.
        public IActionResult Listar(string nombreProyecto, string nombreTabla)
        {
            // Si el nombre de la tabla está vacío o en blanco, retorna un error 400 (BadRequest).
            if (string.IsNullOrWhiteSpace(nombreTabla))
                return BadRequest("El nombre de la tabla no puede estar vacío.");

            try
            {
                // Paso 1: Construir la parte base del SELECT (consulta SQL).
                // Se seleccionan todas las columnas de la tabla principal.
                string selectBase = $"SELECT {nombreTabla}.*";

                // Variable que almacenará los posibles JOINs con otras tablas.
                string joins = "";

                // Paso 2: Obtener todas las columnas de la tabla que se va a consultar.
                controlConexion.AbrirBd(); // Abre la conexión a la base de datos.

                var columnasTabla = controlConexion.EjecutarConsultaSql($@"
                    SELECT COLUMN_NAME 
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = '{nombreTabla}'", null); // Consulta al diccionario de datos del sistema.

                controlConexion.CerrarBd(); // Cierra la conexión a la base de datos.

                // Recorre cada columna de la tabla obtenida.
                foreach (DataRow col in columnasTabla.Rows)
                {
                    // Convierte el nombre de la columna a string.
                    string colName = col["COLUMN_NAME"].ToString() ?? "";

                    // Si la columna empieza por "fkid", se asume que es una llave foránea.
                    if (colName.StartsWith("fkid"))
                    {
                        // Se extrae el nombre de la tabla relacionada quitando el "fkid".
                        string tablaRelacionada = colName.Substring(4); // Ejemplo: fkidactor -> actor

                        // Se obtiene el nombre de la columna descriptiva de la tabla relacionada (ej. "nombre").
                        string? columnaDesc = ObtenerColumnaDescripcion(tablaRelacionada);

                        // Si se encontró una columna descriptiva en la tabla relacionada...
                        if (!string.IsNullOrEmpty(columnaDesc))
                        {
                            // Se añade esa descripción al SELECT con un alias claro.
                            selectBase += $", {tablaRelacionada}.{columnaDesc} AS {colName}_descripcion";

                            // Se añade un LEFT JOIN para unir con la tabla relacionada,
                            // usando TRY_CAST para evitar errores de tipos de datos.
                            joins += $@"
                        LEFT JOIN {tablaRelacionada}
                        ON TRY_CAST({nombreTabla}.{colName} AS NVARCHAR) = TRY_CAST({tablaRelacionada}.id AS NVARCHAR)";
                        }
                    }
                }

                // Se construye el comando SQL completo con SELECT + JOINs.
                string comandoSQL = $"{selectBase} FROM {nombreTabla} {joins}";

                // Paso 3: Ejecutar la consulta y convertir el resultado a formato JSON.
                var listaFilas = new List<Dictionary<string, object?>>(); // Lista de diccionarios para los resultados.

                controlConexion.AbrirBd(); // Abre conexión a la BD nuevamente.

                // Ejecuta la consulta construida y obtiene una tabla con los datos.
                var tablaResultados = controlConexion.EjecutarConsultaSql(comandoSQL, null);

                controlConexion.CerrarBd(); // Cierra conexión.

                // Recorre cada fila del resultado.
                foreach (DataRow fila in tablaResultados.Rows)
                {
                    // Convierte cada fila en un diccionario (clave: nombre columna, valor: valor del dato).
                    var propiedadesFila = fila.Table.Columns.Cast<DataColumn>()
                        .ToDictionary(
                            columna => columna.ColumnName,
                            columna => fila[columna] == DBNull.Value ? null : fila[columna]);

                    // Agrega el diccionario a la lista de resultados.
                    listaFilas.Add(propiedadesFila);
                }

                // Retorna el resultado en formato JSON con código HTTP 200 (OK).
                return Ok(listaFilas);
            }
            catch (Exception ex)
            {
                // Si hay algún error inesperado, devuelve un código 500 (Error del servidor).
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }


        // METODO AUXILIAR para detectar la mejor columna de texto
        // Método privado que devuelve el nombre de una columna descriptiva (como "descripcion", "nombre", "titulo")
        // de una tabla relacionada, si existe. Retorna null si no encuentra una coincidencia.
        private string? ObtenerColumnaDescripcion(string nombreTablaRelacionada)
        {
            try
            {
                // Abre la conexión a la base de datos.
                controlConexion.AbrirBd();

                // Comando SQL para consultar columnas de tipo texto en la tabla relacionada.
                // Busca solo columnas tipo varchar o nvarchar (usualmente usadas para descripciones).
                string comandoInfo = $@"
                SELECT COLUMN_NAME 
                    FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = '{nombreTablaRelacionada}' 
                    AND DATA_TYPE IN ('varchar', 'nvarchar')";

                // Ejecuta la consulta SQL y obtiene las columnas filtradas.
                var columnas = controlConexion.EjecutarConsultaSql(comandoInfo, null);

                // Cierra la conexión una vez se obtiene la información.
                controlConexion.CerrarBd();

                // Lista de posibles nombres comunes que indican una descripción o texto identificativo.
                string[] posiblesNombres = { "descripcion", "nombre", "titulo" };

                // Recorre cada columna para ver si su nombre coincide con uno de los esperados.
                foreach (DataRow fila in columnas.Rows)
                {
                    // Obtiene el nombre de la columna en minúsculas.
                    string nombreCol = fila["COLUMN_NAME"].ToString()?.ToLower() ?? "";

                    // Si el nombre coincide con uno de los posibles nombres, se devuelve como resultado.
                    if (posiblesNombres.Contains(nombreCol))
                    {
                        return nombreCol;
                    }
                }
            }
            catch
            {
                // Si ocurre un error, asegura cerrar la conexión.
                controlConexion.CerrarBd();
            }

            // Si no se encontró una columna válida o hubo error, retorna null.
            return null;
        }

        [HttpGet("{nombreClave}/{valor}")] // Define una ruta HTTP GET con parámetros adicionales.
        [Authorize(Roles = "admin,Verificador,Validador")] //Define los roles tiene acceso al ENDPOINT
        public IActionResult ObtenerPorClave(string nombreProyecto, string nombreTabla, string nombreClave, string valor) // Método que obtiene una fila específica basada en una clave.
        {
            if (string.IsNullOrWhiteSpace(nombreTabla) || string.IsNullOrWhiteSpace(nombreClave) || string.IsNullOrWhiteSpace(valor)) // Verifica si alguno de los parámetros está vacío.
            {
                return BadRequest("El nombre de la tabla, el nombre de la clave y el valor no pueden estar vacíos."); // Retorna una respuesta de error si algún parámetro está vacío.
            }

            controlConexion.AbrirBd(); // Abre la conexión a la base de datos.
            try
            {
                string proveedor = _configuration["DatabaseProvider"] ?? throw new InvalidOperationException("Proveedor de base de datos no configurado."); // Obtiene el proveedor de base de datos desde la configuración.

                string consultaSQL;
                DbParameter[] parametros;

                // Define la consulta SQL y los parámetros para SQL Server y LocalDB.
                consultaSQL = "SELECT data_type FROM information_schema.columns WHERE table_name = @nombreTabla AND column_name = @nombreColumna";
                parametros = new DbParameter[]
                {
                    CrearParametro("@nombreTabla", nombreTabla),
                    CrearParametro("@nombreColumna", nombreClave)
                };

                Console.WriteLine($"Ejecutando consulta SQL: {consultaSQL} con parámetros: nombreTabla={nombreTabla}, nombreColumna={nombreClave}");

                var resultadoTipoDato = controlConexion.EjecutarConsultaSql(consultaSQL, parametros); // Ejecuta la consulta SQL para determinar el tipo de dato de la clave.

                if (resultadoTipoDato == null || resultadoTipoDato.Rows.Count == 0 || resultadoTipoDato.Rows[0]["data_type"] == DBNull.Value) // Verifica si se obtuvo un resultado válido.
                {
                    return NotFound("No se pudo determinar el tipo de dato."); // Retorna una respuesta de error si no se pudo determinar el tipo de dato.
                }

                string tipoDato = resultadoTipoDato.Rows[0]["data_type"]?.ToString() ?? ""; // Obtiene el tipo de dato de la columna.
                Console.WriteLine($"Tipo de dato detectado para la columna {nombreClave}: {tipoDato}");

                if (string.IsNullOrEmpty(tipoDato)) // Verifica si el tipo de dato es válido.
                {
                    return NotFound("No se pudo determinar el tipo de dato."); // Retorna una respuesta de error si el tipo de dato es inválido.
                }

                object valorConvertido;
                string comandoSQL;

                // Determina cómo tratar el valor y la consulta SQL según el tipo de dato, compatible con SQL Server y LocalDB.
                switch (tipoDato.ToLower())
                {
                    case "int":
                    case "bigint":
                    case "smallint":
                    case "tinyint":
                        if (int.TryParse(valor, out int valorEntero))
                        {
                            valorConvertido = valorEntero;
                            comandoSQL = $"SELECT * FROM {nombreTabla} WHERE {nombreClave} = @Valor";
                        }
                        else
                        {
                            return BadRequest("El valor proporcionado no es válido para el tipo de datos entero.");
                        }
                        break;
                    case "decimal":
                    case "numeric":
                    case "money":
                    case "smallmoney":
                        if (decimal.TryParse(valor, out decimal valorDecimal))
                        {
                            valorConvertido = valorDecimal;
                            comandoSQL = $"SELECT * FROM {nombreTabla} WHERE {nombreClave} = @Valor";
                        }
                        else
                        {
                            return BadRequest("El valor proporcionado no es válido para el tipo de datos decimal.");
                        }
                        break;
                    case "bit":
                        if (bool.TryParse(valor, out bool valorBooleano))
                        {
                            valorConvertido = valorBooleano;
                            comandoSQL = $"SELECT * FROM {nombreTabla} WHERE {nombreClave} = @Valor";
                        }
                        else
                        {
                            return BadRequest("El valor proporcionado no es válido para el tipo de datos booleano.");
                        }
                        break;
                    case "float":
                    case "real":
                        if (double.TryParse(valor, out double valorDoble))
                        {
                            valorConvertido = valorDoble;
                            comandoSQL = $"SELECT * FROM {nombreTabla} WHERE {nombreClave} = @Valor";
                        }
                        else
                        {
                            return BadRequest("El valor proporcionado no es válido para el tipo de datos flotante.");
                        }
                        break;
                    case "nvarchar":
                    case "varchar":
                    case "nchar":
                    case "char":
                    case "text":
                        valorConvertido = valor;
                        comandoSQL = $"SELECT * FROM {nombreTabla} WHERE {nombreClave} = @Valor";
                        break;
                    case "date":
                    case "datetime":
                    case "datetime2":
                    case "smalldatetime":
                        if (DateTime.TryParse(valor, out DateTime valorFecha))
                        {
                            comandoSQL = $"SELECT * FROM {nombreTabla} WHERE CAST({nombreClave} AS DATE) = @Valor";
                            valorConvertido = valorFecha.Date;
                        }
                        else
                        {
                            return BadRequest("El valor proporcionado no es válido para el tipo de datos fecha.");
                        }
                        break;
                    default:
                        return BadRequest($"Tipo de dato no soportado: {tipoDato}"); // Retorna un error si el tipo de dato no es soportado.
                }

                var parametro = CrearParametro("@Valor", valorConvertido); // Crea el parámetro para la consulta SQL.

                Console.WriteLine($"Ejecutando consulta SQL: {comandoSQL} con parámetro: {parametro.ParameterName} = {parametro.Value}, DbType: {parametro.DbType}");

                var resultado = controlConexion.EjecutarConsultaSql(comandoSQL, new DbParameter[] { parametro }); // Ejecuta la consulta SQL con el parámetro.

                Console.WriteLine($"DataSet completado para la consulta: {comandoSQL}");

                if (resultado.Rows.Count > 0) // Verifica si hay filas en el resultado.
                {
                    var lista = new List<Dictionary<string, object?>>();
                    foreach (DataRow fila in resultado.Rows)
                    {
                        var propiedades = resultado.Columns.Cast<DataColumn>()
                                           .ToDictionary(columna => columna.ColumnName, columna => fila[columna] == DBNull.Value ? null : fila[columna]);
                        lista.Add(propiedades);
                    }

                    return Ok(lista); // Retorna las filas encontradas en formato JSON.
                }

                return NotFound(); // Retorna un error 404 si no se encontraron filas.
            }
            catch (Exception ex) // Captura cualquier excepción que ocurra durante la ejecución.
            {
                Console.WriteLine($"Ocurrió una excepción: {ex.Message}");
                return StatusCode(500, $"Error interno del servidor: {ex.Message}"); // Retorna un error 500 si ocurre una excepción.
            }
            finally
            {
                controlConexion.CerrarBd(); // Cierra la conexión a la base de datos.
            }
        }

        // Método privado para convertir un JsonElement en su tipo correspondiente.
        private object? ConvertirJsonElement(JsonElement elementoJson)
        {
            if (elementoJson.ValueKind == JsonValueKind.Null)
                return null; // Si el valor es nulo, retorna null.

            switch (elementoJson.ValueKind)
            {
                case JsonValueKind.String:
                    // Intenta convertir la cadena a un valor de tipo DateTime, si falla, retorna la cadena original.
                    return DateTime.TryParse(elementoJson.GetString(), out DateTime valorFecha) ? (object)valorFecha : elementoJson.GetString();
                case JsonValueKind.Number:
                    // Intenta convertir el número a un valor entero, si falla, retorna el valor como doble.
                    return elementoJson.TryGetInt32(out var valorEntero) ? (object)valorEntero : elementoJson.GetDouble();
                case JsonValueKind.True:
                    return true; // Retorna verdadero si el valor es de tipo booleano verdadero.
                case JsonValueKind.False:
                    return false; // Retorna falso si el valor es de tipo booleano falso.
                case JsonValueKind.Null:
                    return null; // Retorna null si el valor es nulo.
                case JsonValueKind.Object:
                    return elementoJson.GetRawText(); // Retorna el texto crudo del objeto JSON.
                case JsonValueKind.Array:
                    return elementoJson.GetRawText(); // Retorna el texto crudo del arreglo JSON.
                default:
                    // Lanza una excepción si el tipo de valor JSON no está soportado.
                    throw new InvalidOperationException($"Tipo de JsonValueKind no soportado: {elementoJson.ValueKind}");
            }
        }

        [HttpPost] // Define una ruta HTTP POST para este método.
        [Authorize(Roles = "admin")] //Solo el administrador puede crear nuevas entidades
        public IActionResult Crear(string nombreProyecto, string nombreTabla, [FromBody] Dictionary<string, object?> datosEntidad)  // Crea una nueva fila en la tabla especificada.
        {
            if (string.IsNullOrWhiteSpace(nombreTabla) || datosEntidad == null || !datosEntidad.Any())  // Verifica si el nombre de la tabla o los datos están vacíos.
                return BadRequest("El nombre de la tabla y los datos de la entidad no pueden estar vacíos.");  // Retorna un error si algún parámetro está vacío.

            try
            {
                var propiedades = datosEntidad.ToDictionary(  // Convierte los datos de la entidad en un diccionario de propiedades.
                    kvp => kvp.Key,
                    kvp => kvp.Value is JsonElement elementoJson ? ConvertirJsonElement(elementoJson) : kvp.Value);

                // Verifica si hay un campo de contraseña en los datos, y si lo hay, lo hashea.
                var clavesContrasena = new[] { "password", "contrasena", "passw", "clave" };  // Lista de posibles nombres para campos de contraseña.
                var claveContrasena = propiedades.Keys.FirstOrDefault(k => clavesContrasena.Any(pk => k.IndexOf(pk, StringComparison.OrdinalIgnoreCase) >= 0));  // Busca si alguno de los campos es una contraseña.

                if (claveContrasena != null)  // Si se encontró un campo de contraseña.
                {
                    var contrasenaPlano = propiedades[claveContrasena]?.ToString();  // Obtiene el valor de la contraseña.
                    if (!string.IsNullOrEmpty(contrasenaPlano))  // Si la contraseña no está vacía.
                    {
                        propiedades[claveContrasena] = BCrypt.Net.BCrypt.HashPassword(contrasenaPlano);  // Hashea la contraseña.
                    }
                }

                string proveedor = _configuration["DatabaseProvider"] ?? throw new InvalidOperationException("Proveedor de base de datos no configurado.");  // Obtiene el proveedor de base de datos.
                var columnas = string.Join(",", propiedades.Keys);  // Une los nombres de las columnas en una cadena.
                var valores = string.Join(",", propiedades.Keys.Select(k => $"{ObtenerPrefijoParametro(proveedor)}{k}"));  // Une los nombres de los valores en una cadena con su prefijo.
                string consultaSQL = $"INSERT INTO {nombreTabla} ({columnas}) VALUES ({valores})";  // Crea la consulta SQL para insertar una nueva fila.

                var parametros = propiedades.Select(p => CrearParametro($"{ObtenerPrefijoParametro(proveedor)}{p.Key}", p.Value)).ToArray();  // Crea los parámetros para la consulta SQL.

                Console.WriteLine($"Ejecutando consulta SQL: {consultaSQL} con parámetros:");  // Muestra la consulta SQL y los parámetros en la consola.
                foreach (var parametro in parametros)  // Recorre cada parámetro.
                {
                    Console.WriteLine($"{parametro.ParameterName} = {parametro.Value}, DbType: {parametro.DbType}");  // Muestra el nombre y valor del parámetro en la consola.
                }

                controlConexion.AbrirBd();  // Abre la conexión a la base de datos.
                controlConexion.EjecutarComandoSql(consultaSQL, parametros);  // Ejecuta la consulta SQL para insertar la nueva fila.
                controlConexion.CerrarBd();  // Cierra la conexión a la base de datos.

                return Ok("Entidad creada exitosamente.");  // Retorna una respuesta de éxito.
            }
            catch (Exception ex)  // Captura cualquier excepción que ocurra durante la ejecución.
            {
                Console.WriteLine($"Ocurrió una excepción: {ex.Message}");  // Muestra el mensaje de la excepción en la consola.
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");  // Retorna un error 500 si ocurre una excepción.
            }
        }


        [HttpPut("{nombreClave}/{valorClave}")] // Define una ruta HTTP PUT con parámetros adicionales.
        [Authorize(Roles = "admin,Validador")] // Solo estos dos roles pueden modificar entidades
        public IActionResult Actualizar(string nombreProyecto, string nombreTabla, string nombreClave, string valorClave, [FromBody] Dictionary<string, object?> datosEntidad) // Actualiza una fila en la tabla basada en una clave.
        {
            if (string.IsNullOrWhiteSpace(nombreTabla) || string.IsNullOrWhiteSpace(nombreClave) || datosEntidad == null || !datosEntidad.Any()) // Verifica si alguno de los parámetros está vacío.
                return BadRequest("El nombre de la tabla, el nombre de la clave y los datos de la entidad no pueden estar vacíos."); // Retorna un error si algún parámetro está vacío.

            try
            {
                var propiedades = datosEntidad.ToDictionary( // Convierte los datos de la entidad en un diccionario de propiedades.
                    kvp => kvp.Key,
                    kvp => kvp.Value is JsonElement elementoJson ? ConvertirJsonElement(elementoJson) : kvp.Value);

                // Verifica si hay un campo de contraseña en los datos, y si lo hay, lo hashea.
                var clavesContrasena = new[] { "password", "contrasena", "passw", "clave" }; // Lista de posibles nombres para campos de contraseña.
                var claveContrasena = propiedades.Keys.FirstOrDefault(k => clavesContrasena.Any(pk => k.IndexOf(pk, StringComparison.OrdinalIgnoreCase) >= 0)); // Busca si alguno de los campos es una contraseña.

                if (claveContrasena != null) // Si se encontró un campo de contraseña.
                {
                    var contrasenaPlano = propiedades[claveContrasena]?.ToString(); // Obtiene el valor de la contraseña.
                    if (!string.IsNullOrEmpty(contrasenaPlano)) // Si la contraseña no está vacía.
                    {
                        propiedades[claveContrasena] = BCrypt.Net.BCrypt.HashPassword(contrasenaPlano); // Hashea la contraseña.
                    }
                }

                string proveedor = _configuration["DatabaseProvider"] ?? throw new InvalidOperationException("Proveedor de base de datos no configurado."); // Obtiene el proveedor de base de datos.
                var actualizaciones = string.Join(",", propiedades.Select(p => $"{p.Key}={ObtenerPrefijoParametro(proveedor)}{p.Key}")); // Crea la cadena de actualizaciones para la consulta SQL.
                string consultaSQL = $"UPDATE {nombreTabla} SET {actualizaciones} WHERE {nombreClave}={ObtenerPrefijoParametro(proveedor)}ValorClave"; // Crea la consulta SQL para actualizar la fila.

                var parametros = propiedades.Select(p => CrearParametro($"{ObtenerPrefijoParametro(proveedor)}{p.Key}", p.Value)).ToList(); // Crea los parámetros para la consulta SQL.
                parametros.Add(CrearParametro($"{ObtenerPrefijoParametro(proveedor)}ValorClave", valorClave)); // Agrega el parámetro para la clave de la fila a actualizar.

                Console.WriteLine($"Ejecutando consulta SQL: {consultaSQL} con parámetros:"); // Muestra la consulta SQL y los parámetros en la consola.
                foreach (var parametro in parametros) // Recorre cada parámetro.
                {
                    Console.WriteLine($"{parametro.ParameterName} = {parametro.Value}, DbType: {parametro.DbType}"); // Muestra el nombre y valor del parámetro en la consola.
                }

                controlConexion.AbrirBd(); // Abre la conexión a la base de datos.
                controlConexion.EjecutarComandoSql(consultaSQL, parametros.ToArray()); // Ejecuta la consulta SQL para actualizar la fila.
                controlConexion.CerrarBd(); // Cierra la conexión a la base de datos.

                return Ok("Entidad actualizada exitosamente."); // Retorna una respuesta de éxito.
            }
            catch (Exception ex) // Captura cualquier excepción que ocurra durante la ejecución.
            {
                Console.WriteLine($"Ocurrió una excepción: {ex.Message}"); // Muestra el mensaje de la excepción en la consola.
                return StatusCode(500, $"Error interno del servidor: {ex.Message}"); // Retorna un error 500 si ocurre una excepción.
            }
        }


        // Método privado para obtener el prefijo adecuado para los parámetros SQL, según el proveedor de la base de datos.
        private string ObtenerPrefijoParametro(string proveedor)
        {
            return "@"; // Para SQL Server y LocalDB, el prefijo es "@". En caso de otros proveedores, se pueden agregar más condiciones aquí.
        }

        [HttpDelete("{nombreClave}/{valorClave}")] // Define una ruta HTTP DELETE con parámetros adicionales.
        [Authorize(Roles = "admin")] //Solo el administrador puede borrar entidades
        public IActionResult Eliminar(string nombreProyecto, string nombreTabla, string nombreClave, string valorClave) // Elimina una fila de la tabla basada en una clave.
        {
            if (string.IsNullOrWhiteSpace(nombreTabla) || string.IsNullOrWhiteSpace(nombreClave)) // Verifica si alguno de los parámetros está vacío.
                return BadRequest("El nombre de la tabla o el nombre de la clave no pueden estar vacíos."); // Retorna un error si algún parámetro está vacío.

            try
            {
                string proveedor = _configuration["DatabaseProvider"] ?? throw new InvalidOperationException("Proveedor de base de datos no configurado."); // Obtiene el proveedor de base de datos.
                string consultaSQL = $"DELETE FROM {nombreTabla} WHERE {nombreClave}=@ValorClave"; // Crea la consulta SQL para eliminar la fila.
                var parametro = CrearParametro("@ValorClave", valorClave); // Crea el parámetro para la clave de la fila a eliminar.

                controlConexion.AbrirBd(); // Abre la conexión a la base de datos.
                controlConexion.EjecutarComandoSql(consultaSQL, new[] { parametro }); // Ejecuta la consulta SQL para eliminar la fila.
                controlConexion.CerrarBd(); // Cierra la conexión a la base de datos.

                return Ok("Entidad eliminada exitosamente."); // Retorna una respuesta de éxito.
            }
            catch (Exception ex) // Captura cualquier excepción que ocurra durante la ejecución.
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}"); // Retorna un error 500 si ocurre una excepción.
            }
        }


        [AllowAnonymous] // Permite el acceso anónimo a este método, no requiere autorización, solo es informativo
        [HttpGet("/")] // Define una ruta HTTP GET en la raíz de la API.
        public IActionResult ObtenerRaiz() // Método que retorna un mensaje indicando que la API está en funcionamiento.
        {
            return Ok("La API está en funcionamiento"); // Retorna un mensaje indicando que la API está en funcionamiento.
        }

        [HttpPost("verificar-contrasena")] // Define una ruta HTTP POST para verificar contraseñas.
        [AllowAnonymous] // Permite el acceso anónimo a este método
        public IActionResult VerificarContrasena(string nombreProyecto, string nombreTabla, [FromBody] Dictionary<string, string> datos) // Verifica si la contraseña proporcionada coincide con la almacenada.
        {
                if (string.IsNullOrWhiteSpace(nombreTabla) || datos == null || !datos.ContainsKey("campoUsuario") || !datos.ContainsKey("campoContrasena") || !datos.ContainsKey("valorUsuario") || !datos.ContainsKey("valorContrasena")) // Verifica si alguno de los parámetros está vacío.
                return BadRequest("El nombre de la tabla, el campo de usuario, el campo de contraseña, el valor de usuario y el valor de contraseña no pueden estar vacíos."); // Retorna un error si algún parámetro está vacío.

            try
            {
                string campoUsuario = datos["campoUsuario"]; // Obtiene el nombre del campo de usuario.
                string campoContrasena = datos["campoContrasena"]; // Obtiene el nombre del campo de contraseña.
                string valorUsuario = datos["valorUsuario"]; // Obtiene el valor del usuario.
                string valorContrasena = datos["valorContrasena"]; // Obtiene el valor de la contraseña.

                string proveedor = _configuration["DatabaseProvider"] ?? throw new InvalidOperationException("Proveedor de base de datos no configurado."); // Obtiene el proveedor de base de datos.
                string consultaSQL = $"SELECT {campoContrasena} FROM {nombreTabla} WHERE {campoUsuario} = @ValorUsuario"; // Crea la consulta SQL para obtener la contraseña almacenada.
                var parametro = CrearParametro("@ValorUsuario", valorUsuario); // Crea el parámetro para el valor del usuario.

                controlConexion.AbrirBd(); // Abre la conexión a la base de datos.
                var resultado = controlConexion.EjecutarConsultaSql(consultaSQL, new DbParameter[] { parametro }); // Ejecuta la consulta SQL para obtener la contraseña.
                controlConexion.CerrarBd(); // Cierra la conexión a la base de datos.

                if (resultado.Rows.Count == 0) // Verifica si no se encontró el usuario.
                {
                    return NotFound("Usuario no encontrado."); // Retorna un error 404 si no se encontró el usuario.
                }

                string contrasenaHasheada = resultado.Rows[0][campoContrasena]?.ToString() ?? string.Empty; // Obtiene la contraseña hasheada almacenada.

                // Verifica si el hash de la contraseña es válido.
                if (!contrasenaHasheada.StartsWith("$2"))
                {
                    throw new InvalidOperationException("El hash de la contraseña almacenada no es un hash válido de BCrypt."); // Lanza una excepción si el hash almacenado no es válido.
                }

                bool esContrasenaValida = BCrypt.Net.BCrypt.Verify(valorContrasena, contrasenaHasheada); // Verifica si la contraseña proporcionada coincide con el hash almacenado.

                if (esContrasenaValida) // Si la contraseña es válida.
                {
                    return Ok(esContrasenaValida); // Retorna una respuesta de éxito.
                }
                else // Si la contraseña no es válida.
                {
                    return Unauthorized(esContrasenaValida); // Retorna un error 401 si la contraseña es incorrecta.
                }
            }
            catch (Exception ex) // Captura cualquier excepción que ocurra durante la ejecución.
            {
                Console.WriteLine($"Ocurrió una excepción: {ex.Message}"); // Muestra el mensaje de la excepción en la consola.
                return StatusCode(500, $"Error interno del servidor: {ex.Message}"); // Retorna un error 500 si ocurre una excepción.
            }
        }


        // Método para crear un parámetro de consulta SQL basado en el proveedor de base de datos.
        private DbParameter CrearParametro(string nombre, object? valor)
        {
            return new SqlParameter(nombre, valor ?? DBNull.Value); // Crea un parámetro para SQL Server y LocalDB.
        }


        [HttpPost("ejecutar-consulta-parametrizada")]
        [Authorize(Roles = "admin,Verificador,Validador")] // Los 3 roles tienen acceso a las consultas parametrizadas
        public IActionResult EjecutarConsultaParametrizada([FromBody] JsonElement cuerpoSolicitud)
        {
            try
            {
                // Verifica si el cuerpo de la solicitud contiene la consulta SQL
                if (!cuerpoSolicitud.TryGetProperty("consulta", out var consultaElement) || consultaElement.ValueKind != JsonValueKind.String)
                {
                    return BadRequest("Debe proporcionar una consulta SQL válida en el cuerpo de la solicitud.");
                }

                string consultaSQL = consultaElement.GetString() ?? throw new ArgumentException("La consulta SQL no puede estar vacía.");

                // Verifica si el cuerpo de la solicitud contiene los parámetros
                var parametros = new List<DbParameter>();
                if (cuerpoSolicitud.TryGetProperty("parametros", out var parametrosElement) && parametrosElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var parametro in parametrosElement.EnumerateObject())
                    {
                        string paramName = parametro.Name.StartsWith("@") ? parametro.Name : "@" + parametro.Name;
                        object? paramValue = parametro.Value.ValueKind == JsonValueKind.Null ? DBNull.Value : parametro.Value.GetRawText().Trim('"');
                        parametros.Add(controlConexion.CrearParametro(paramName, paramValue));
                    }
                }

                // Abrir la conexión a la base de datos
                controlConexion.AbrirBd();

                // Ejecutar la consulta SQL
                var resultado = controlConexion.EjecutarConsultaSql(consultaSQL, parametros.ToArray());

                // Cerrar la conexión a la base de datos
                controlConexion.CerrarBd();

                // Verifica si hay resultados
                if (resultado.Rows.Count == 0)
                {
                    return NotFound("No se encontraron resultados para la consulta proporcionada.");
                }

                // Procesar resultados a formato JSON
                var lista = new List<Dictionary<string, object?>>();
                foreach (DataRow fila in resultado.Rows)
                {
                    var propiedades = resultado.Columns.Cast<DataColumn>()
                        .ToDictionary(col => col.ColumnName, col => fila[col] == DBNull.Value ? null : fila[col]);
                    lista.Add(propiedades);
                }

                // Retornar resultados en formato JSON
                return Ok(lista);
            }
            catch (SqlException sqlEx)
            {
                // Manejo de excepciones SQL
                controlConexion.CerrarBd(); // Asegura que la conexión se cierre en caso de error
                Console.WriteLine($"SQL Error: {sqlEx.Message}");
                return StatusCode(500, new { Mensaje = "Error en la base de datos.", Detalle = sqlEx.Message });
            }
            catch (Exception ex)
            {
                // Manejo de excepciones generales
                controlConexion.CerrarBd(); // Asegura que la conexión se cierre en caso de error
                Console.WriteLine($"Error: {ex.Message}");
                return StatusCode(500, new { Mensaje = "Se presentó un error:", Detalle = ex.Message });
            }
        }


        [HttpPost("ejecutar-procedimiento/{procedureName}")]
        [Authorize(Roles = "admin,Verificador,Validador")] // Solo los 3 roles tienen acceso al método
        public IActionResult EjecutarProcedimientoAlmacenado(string procedureName, [FromBody] JsonElement body)
        {
            // Verificar que el nombre del procedimiento no esté vacío
            if (string.IsNullOrWhiteSpace(procedureName))
            {
                return BadRequest(new { Mensaje = "El nombre del procedimiento es requerido." });
            }

            try
            {
                // Abrir la conexión a la base de datos
                controlConexion.AbrirBd();

                // Obtener la conexión
                var connection = controlConexion.ObtenerConexion();
                if (connection == null || connection.State != ConnectionState.Open)
                {
                    return StatusCode(500, "No se pudo obtener una conexión válida a la base de datos.");
                }

                using (var command = new SqlCommand(procedureName, (SqlConnection)connection))
                {
                    command.CommandType = CommandType.StoredProcedure;

                    // Agregar parámetros al comando
                    foreach (var property in body.EnumerateObject())
                    {
                        string paramName = property.Name.StartsWith("@") ? property.Name : "@" + property.Name;
                        if (property.Name.EndsWith("productos") && property.Value.ValueKind == JsonValueKind.Array)
                        {
                            var productosJson = JsonSerializer.Serialize(property.Value);
                            command.Parameters.AddWithValue(paramName, productosJson);
                        }
                        else
                        {
                            command.Parameters.AddWithValue(paramName, property.Value.GetRawText().Trim('"'));
                        }
                    }

                    // Ejecutar el procedimiento almacenado
                    int filasAfectadas = command.ExecuteNonQuery();
                    controlConexion.CerrarBd(); // Cerrar la conexión a la base de datos

                    return Ok(new { Mensaje = "Procedimiento almacenado ejecutado exitosamente.", FilasAfectadas = filasAfectadas });
                }
            }
            catch (SqlException sqlEx)
            {
                controlConexion.CerrarBd(); // Asegura cerrar la conexión en caso de error
                Console.WriteLine($"SQL Error: {sqlEx.Message}");
                return StatusCode(500, new { Mensaje = "Error en la base de datos.", Detalle = sqlEx.Message });
            }
            catch (Exception ex)
            {
                controlConexion.CerrarBd(); // Asegura cerrar la conexión en caso de error general
                Console.WriteLine($"Error: {ex.Message}");
                return StatusCode(500, new { Mensaje = "Se presentó Un error:", Detalle = ex.Message });
            }
        }

        [HttpGet("validar")]
        [Authorize]
        public IActionResult ValidarToken()
        {
            var user = HttpContext.User;

            if (user.Identity != null && user.Identity.IsAuthenticated)
            {
                var expClaim = user.Claims.FirstOrDefault(c => c.Type == "exp");

                if (expClaim != null && long.TryParse(expClaim.Value, out long exp))
                {
                    var expirationDate = DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime;
                    if (expirationDate > DateTime.UtcNow)
                    {
                        return Ok(new
                        {
                            Mensaje = "Token válido",
                            Usuario = user.Identity.Name,
                            ExpiraEn = expirationDate
                        });
                    }
                    return Unauthorized(new { Mensaje = "Token expirado" });
                }

                return Ok(new
                {
                    Mensaje = "Token válido, sin información de expiración"
                });
            }

            return Unauthorized(new { Mensaje = "Token inválido o no autenticado" });
        }

        //CONSULTAS DE LA ENTREGA NUMERO 3 

        //CONSULTA 1
        //Mostrar el listado de los campos de la tabla indicador, incluyendo el nombre del tipo, 
        //nombre del sentido y la descripción de la unidad de medición de cada indicador. 
        [HttpGet("consulta/indicadores-con-detalle")]
        [Authorize(Roles = "admin,Verificador,Validador")] // Los 3 roles tienen acceso a las consultas
        public IActionResult IndicadoresConDetalle(string nombreProyecto)
        {
            try
            {
                string sql = @"
                    SELECT i.*, 
                        t.nombre AS tipo_nombre, 
                        s.nombre AS sentido_nombre, 
                        u.descripcion AS unidad_descripcion
                    FROM indicador i
                    JOIN tipoindicador t ON i.fkidtipoindicador = t.id
                    JOIN sentido s ON i.fkidsentido = s.id
                    JOIN unidadmedicion u ON i.fkidunidadmedicion = u.id";

                return EjecutarConsultaPersonalizada(sql);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        //CONSULTA 2
        //Mostrar id, nombre código, objetivo, fórmula y meta de los indicadores, incluyendo el nombre de las representación visual. 

        [HttpGet("consulta/indicadores-representacion")]
        [Authorize(Roles = "admin,Verificador,Validador")]
        public IActionResult IndicadoresConRepresentacion(string nombreProyecto)
        {
            try
            {
                string sql = @"
                    SELECT i.id, i.nombre, i.codigo, i.objetivo, i.formula, i.meta, 
                        rv.nombre AS representacion_visual
                    FROM indicador i
                    JOIN represenvisualporindicador rpi ON i.id = rpi.fkidindicador
                    JOIN represenvisual rv ON rpi.fkidrepresenvisual = rv.id";

                return EjecutarConsultaPersonalizada(sql);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        //CONSULTA 3
        //Mostrar id, nombre código, objetivo, fórmula y meta de los indicadores, 
        //incluyendo el nombre de los responsables (actores) incluyendo el nombre del tipo actor. 
        [HttpGet("consulta/indicadores-actores")]
        [Authorize(Roles = "admin,Verificador,Validador")] // Los 3 roles tienen acceso a las consultas
        public IActionResult IndicadoresConActores(string nombreProyecto)
        {
            try
            {
                string sql = @"
                    SELECT i.id, i.nombre, i.codigo, i.objetivo, i.formula, i.meta, 
                        a.nombre AS actor_nombre, 
                        ta.nombre AS tipo_actor
                    FROM indicador i
                    JOIN responsablesporindicador rpi ON i.id = rpi.fkidindicador
                    JOIN actor a ON rpi.fkidresponsable = a.id
                    JOIN tipoactor ta ON a.fkidtipoactor = ta.id";

                return EjecutarConsultaPersonalizada(sql);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        //CONSULTA 4
        //Mostrar id, nombre código, objetivo, fórmula y meta de los indicadores, incluyendo el nombre de las fuentes. 
        [HttpGet("consulta/indicadores-fuentes")]
        [Authorize(Roles = "admin,Verificador,Validador")] // Los 3 roles tienen acceso a las consultas
        public IActionResult IndicadoresConFuentes(string nombreProyecto)
        {
            try
            {
                string sql = @"
                    SELECT i.id, i.nombre, i.codigo, i.objetivo, i.formula, i.meta, 
                        f.nombre AS fuente_nombre
                    FROM indicador i
                    JOIN fuentesporindicador fpi ON i.id = fpi.fkidindicador
                    JOIN fuente f ON fpi.fkidfuente = f.id";

                return EjecutarConsultaPersonalizada(sql);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        //CONSULTA 5
        //Mostrar id, nombre código, objetivo, fórmula y meta de los indicadores, incluyendo el nombre de las variables, 
        //el dato de cada variable y la fecha de cada dato. 
        [HttpGet("consulta/indicadores-variables")]
        [Authorize(Roles = "admin,Verificador,Validador")] // Los 3 roles tienen acceso a las consultas
        public IActionResult IndicadoresConVariables(string nombreProyecto)
        {
            try
            {
                string sql = @"
                    SELECT i.id, i.nombre, i.codigo, i.objetivo, i.formula, i.meta,
                        v.nombre AS variable_nombre,
                        vpi.dato, vpi.fechadato
                    FROM indicador i
                    JOIN variablesporindicador vpi ON i.id = vpi.fkidindicador
                    JOIN variable v ON vpi.fkidvariable = v.id";

                return EjecutarConsultaPersonalizada(sql);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        //CONSULTA 6
        //Mostrar el listado de los campos de la tabla indicador, incluyendo el id del resultado, 
        //el resultado y la fecha de cálculo de cada resultado. 

        [HttpGet("consulta/indicadores-resultados")]
        [Authorize(Roles = "admin,Verificador,Validador")] // Los 3 roles tienen acceso a las consultas
        public IActionResult IndicadoresConResultados(string nombreProyecto)
        {
            try
            {
                string sql = @"
                    SELECT i.*, 
                        ri.id AS id_resultado, 
                        ri.resultado, 
                        ri.fechacalculo
                    FROM indicador i
                    JOIN resultadoindicador ri ON i.id = ri.fkidindicador";

                return EjecutarConsultaPersonalizada(sql);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }


        //Método para evitar la duplicidad en el código de las consultas SQL
        private IActionResult EjecutarConsultaPersonalizada(string sql)
        {
            var listaFilas = new List<Dictionary<string, object?>>();
            controlConexion.AbrirBd();
            var tablaResultados = controlConexion.EjecutarConsultaSql(sql, null);
            controlConexion.CerrarBd();

            foreach (DataRow fila in tablaResultados.Rows)
            {
                var filaDict = fila.Table.Columns.Cast<DataColumn>()
                    .ToDictionary(col => col.ColumnName, col => fila[col] == DBNull.Value ? null : fila[col]);
                listaFilas.Add(filaDict);
            }

            return Ok(listaFilas);
        }

    }
}

