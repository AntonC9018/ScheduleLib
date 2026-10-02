namespace ScheduleLib;

public static class SpecializationExtensions
{
    extension(Specialization)
    {
        // ReSharper disable once InconsistentNaming
        public static Specialization AG => new("AG");
        public static Specialization AlgoritmicaGrafurilor => new("Algoritmica Grafurilor");
        public static Specialization CV => new("CV");
        public static Specialization DezvoltareaAplicatiilor => new("DezvoltareaAplicatiilor");
        public static Specialization DJ => new("DJ");
        public static Specialization GA2D => new("GA2D");
        public static Specialization GA3D => new("GA3D");
        public static Specialization Logica => new("Logica");
        public static Specialization React => new("React");
        public static Specialization Spring => new("Spring");
        public static Specialization SSI => new("SSI");
        public static Specialization UI => new("UI");
    }
}
