namespace PIndicadores.Models
{
    public class Resultadoindicador
    {
        public int id { get; set; }
        public float resultado { get; set; }
        public DateTime fechacalculo { get; set; }
        public int fkidindicador { get; set; }
    }
}
