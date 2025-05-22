using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Linq;
using PIndicadores.Models;
using PIndicadores.Services;

namespace PIndicadores.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly TokenService _tokenService;
        private readonly ControlConexion _controlConexion;

        public AuthController(TokenService tokenService, ControlConexion controlConexion)
        {
            _tokenService = tokenService;
            _controlConexion = controlConexion;
        }

        [AllowAnonymous]
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginModel login)
        {
            _controlConexion.AbrirBd();
            string comandoSQL = "SELECT email FROM usuario WHERE email = @Email";
            var parametros = new[]
            {
                new SqlParameter("@Email", login.Email),
                //new SqlParameter("@Contrasena", login.Contrasena)
            };
            var result = _controlConexion.EjecutarConsultaSql(comandoSQL, parametros);
            _controlConexion.CerrarBd();

            if (result.Rows.Count == 1)
            {
                var roles = ObtenerRolesUsuario(login.Email);
                var rutas = ObtenerRutasPorRoles(roles);
                var token = _tokenService.GenerateToken(login.Email, roles);

                return Ok(new
                {
                    Email = login.Email,
                    Roles = roles,
                    Rutas = rutas,
                    Token = token
                });
            }

            return Unauthorized("Email o contraseña incorrectos, valide por favor.");
        }

        [AllowAnonymous]
        [HttpGet("obtener-roles")]
        public IActionResult ObtenerRolesPorUsuario([FromQuery] string email)
        {
            var roles = ObtenerRolesUsuario(email);

            if (roles != null && roles.Count > 0)
            {
                return Ok(new { Roles = roles });
            }

            return NotFound("No se encontraron roles para el usuario.");
        }

        private List<string> ObtenerRolesUsuario(string email)
        {
            var roles = new List<string>();

            _controlConexion.AbrirBd();
            string comandoSQL = @"
                SELECT rol.nombre 
                FROM usuario 
                INNER JOIN rol_usuario ON usuario.email = rol_usuario.fkemail
                INNER JOIN rol ON rol_usuario.fkidrol = rol.id 
                WHERE usuario.email = @Email";

            var parametros = new[]
            {
                new SqlParameter("@Email", email)
            };

            var result = _controlConexion.EjecutarConsultaSql(comandoSQL, parametros);
            _controlConexion.CerrarBd();

            foreach (DataRow row in result.Rows)
            {
                roles.Add(row["nombre"].ToString());
            }

            return roles;
        }

        private List<string> ObtenerRutasPorRoles(List<string> roles)
        {
            var rutas = new List<string>();

            foreach (var rol in roles)
            {
                switch (rol)
                {
                    case "admin":
                        rutas.AddRange(new[]
                        {
                            /*"usuarios/gestion",
                            "indicadores/gestion",
                            "modulos/administracion",
                            "modulos/configuracion"*/
                            "actor",
                            "articulo",
                            "frecuencia",
                            "fuente",
                            "fuentesporindicador",
                            "indicador",
                            "literal",
                            "numeral",
                            "paragrafo",
                            "represenvisual",
                            "represenvisualporindicador",
                            "responsablesporindicador",
                            "resultadoindicador",
                            "rol",
                            "rol_Usuario",
                            "seccion",
                            "sentido",
                            "subseccion",
                            "tipoactor",
                            "tipoindicador",
                            "unidadmedicion",
                            "usuario",
                            "variable",
                            "variablesporindicador"
                        });
                        break;
                    case "Verificador":
                        rutas.AddRange(new[]
                       {
                            //"indicadores/consulta"//
                            "actor",
                            "articulo",
                            "frecuencia",
                            "fuente",
                            "fuentesporindicador",
                            "indicador",
                            "literal",
                            "numeral",
                            "paragrafo",
                            "represenvisual",
                            "represenvisualporindicador",
                            "responsablesporindicador",
                            "resultadoindicador",
                            "seccion",
                            "sentido",
                            "subseccion",
                            "tipoactor",
                            "tipoindicador",
                            "unidadmedicion",
                            "variable",
                            "variablesporindicador"
                        });

                        break;

                    case "Validador":
                        rutas.AddRange(new[]
                        {
                            /*"indicadores/consulta",
                            "indicadores/modificar"*/
                            "actor",
                            "articulo",
                            "frecuencia",
                            "fuente",
                            "fuentesporindicador",
                            "indicador",
                            "literal",
                            "numeral",
                            "paragrafo",
                            "represenvisual",
                            "represenvisualporindicador",
                            "responsablesporindicador",
                            "resultadoindicador",
                            "reccion",
                            "sentido",
                            "subseccion",
                            "tipoactor",
                            "tipoindicador",
                            "unidadmedicion",
                            "variable",
                            "variablesporindicador"
                        });
                        break;

                    case "Administrativo":
                        rutas.AddRange(new[]
                        {
                            /*"usuarios/consulta",
                            "indicadores/consulta",
                            "modulos/consulta"*/
                            "actor",
                            "articulo",
                            "frecuencia",
                            "fuente",
                            "fuentesporindicador",
                            "indicador",
                            "literal",
                            "numeral",
                            "paragrafo",
                            "represenvisual",
                            "represenvisualporindicador",
                            "responsablesporindicador",
                            "resultadoindicador",
                            "seccion",
                            "sentido",
                            "subseccion",
                            "tipoactor",
                            "tipoindicador",
                            "unidadmedicion",
                            "variable",
                            "variablesporindicador"
                        });
                        break;

                    case "invitado":
                        rutas.Add("menu"); // Solo puede ver el menú
                        break;

                    default:
                        // Rol desconocido: no se agregan rutas
                        break;
                }
            }
            return rutas.Distinct().ToList();
        }
    }
}