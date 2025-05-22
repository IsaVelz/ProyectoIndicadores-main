namespace PIndicadores.Models
{
    public class Variablesporindicador
    {
        public int id { get; set; }
        public int fkidvariable { get; set; }
        public int fkidindicador { get; set; }
        public float dato { get; set; }
        public string? fkemailusuario { get; set; }
        public DateTime fechadato { get; set; }
    }
}
