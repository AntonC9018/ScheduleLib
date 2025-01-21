using ScheduleLib;
using ScheduleLib.Generation;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

var timeConfig = LessonTimeConfig.CreateDefault();

var schedule = ScheduleBuilder.Create(schedule =>
{
    schedule.SetStudyYear(2024);

    var m2201 = schedule.Group("M2201(ro)");
    var i2201 = schedule.Group("I2201(ro)");
    var ia2201 = schedule.Group("IA2201(ro)");

    var v_gutu = schedule.Teacher("V.Gutu");
    var v_patiuc = schedule.Teacher("V.Patiuc");
    var a_perjan = schedule.Teacher("A.Perjan");
    var a_postaru = schedule.Teacher("A.Postaru");
    var a_tkacenko = schedule.Teacher("A.Tkacenko");
    var i_bat = schedule.Teacher("A.Bat");
    var gh_rusu = schedule.Teacher("Gh.Rusu");
    var p_sarbu = schedule.Teacher("P.Sarbu");
    var i_verlan = schedule.Teacher("I.Verlan");
    var a_prepelita = schedule.Teacher("A.Prepelita");
    var m_cristei = schedule.Teacher("M.Cristei");
    var v_ungureanu = schedule.Teacher("V.Ungureanu");
    var i_anghelov = schedule.Teacher("I.Anghelov");
    var v_visnevschi = schedule.Teacher("V.Visnevschi");
    var n_nartea = schedule.Teacher("N.Nartea");
    var n_plesca = schedule.Teacher("N.Plesca");
    var m_butnaru = schedule.Teacher("M.Butnaru");
    var v_uncureanu = schedule.Teacher("V.Uncureanu");

    var pachetOptional = schedule.Course("Pachet opțional");
    var calculVariational = schedule.Course("Calcul variațional");
    var cloudComputing = schedule.Course("Cloud Computing");
    var elaborareAplicatiiGrafice = schedule.Course("Elaborarea Aplicațiilor Grafice", "Elab. Aplic. Grafice");
    var computeAlgebra = schedule.Course("Sisteme de Algebra Computationala", "Sist. de Alg. Comp.");
    var grafica = schedule.Course("Grafica pe calculator", "Grafica pe calc.");
    var webSecurity = schedule.Course("Securitatea aplicatiilor Web", "SAW");
    var psi = schedule.Course("Proiectarea Sistemelor Informatice", "PSI");
    var framework = schedule.Course("Framework");
    var java = schedule.Course("Tehnologii Java p/u Internet", "Java p/u Internet");
    var cercetariOperationale = schedule.Course("Cercetari operationale");
    var modelareMatematica = schedule.Course("Modelare matematica");
    var aplicatiiWeb = schedule.Course("Dezvoltarea aplicatiilor Web", "Dezv. aplic. Web");

    var room419_4 = schedule.Room("419/4");
    var room113_4 = schedule.Room("113/4");
    var room213a_4 = schedule.Room("213a/4");
    var room222_4 = schedule.Room("222/4");
    var room237_4 = schedule.Room("237/4");
    var room214_4 = schedule.Room("214/4");
    var room404_4 = schedule.Room("404/4");
    var room401_4 = schedule.Room("401/4");
    var room423_4 = schedule.Room("423/4");
    var room143_4 = schedule.Room("143/4");
    var room145_4 = schedule.Room("145/4");
    var room122_4 = schedule.Room("122/4");
    var room350_4 = schedule.Room("450/4");
    var room326_4 = schedule.Room("326/4");
    var room218_4 = schedule.Room("218/4");
    var room218_4a = schedule.Room("218/4a");
    var room254_4 = schedule.Room("254/4");
    var room216a_4a = schedule.Room("216a/4a");
    var room251_4 = schedule.Room("251/4");

    _ = schedule.Scope(s =>
    {
        s.Group(m2201);

        foreach (var t in new[]{ timeConfig.T13_15, timeConfig.T15_00 })
        {
            s.RegularLesson(b =>
            {
                b.Date(new()
                {
                    TimeSlot = t,
                    DayOfWeek = DayOfWeek.Monday,
                });
                b.Course(pachetOptional);
                b.Teacher(v_gutu);
                b.Room(room419_4);
            });
        }

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T11_30,
                DayOfWeek = DayOfWeek.Tuesday,
            });
            b.Course(calculVariational);
            b.Teacher(v_patiuc);
            b.Room(room237_4);
            b.Type(LessonType.Lab);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T13_15,
                DayOfWeek = DayOfWeek.Tuesday,
            });
            b.Course(pachetOptional);
            b.Teacher(a_perjan);
            b.Room(room214_4);
            b.Type(LessonType.Curs);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T15_00,
                DayOfWeek = DayOfWeek.Tuesday,
            });
            b.Course(pachetOptional);
            b.Teacher(a_postaru);
            b.Room(room419_4);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T13_15,
                DayOfWeek = DayOfWeek.Wednesday,
            });
            b.Course(cercetariOperationale);
            b.Type(LessonType.Curs);
            b.Teacher(a_tkacenko);
            b.Room(room419_4);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T15_00,
                DayOfWeek = DayOfWeek.Wednesday,
            });
            b.Course(cercetariOperationale);
            b.Type(LessonType.Seminar);
            b.Teacher(a_tkacenko);
            b.Room(room419_4);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T11_30,
                DayOfWeek = DayOfWeek.Thursday,
            });
            b.Course(pachetOptional);
            b.Room(room214_4);
            b.Teacher(a_perjan);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T13_15,
                DayOfWeek = DayOfWeek.Thursday,
            });
            b.Course(modelareMatematica);
            b.Teacher(i_bat);
            b.Room(room237_4);
            b.Type(LessonType.Lab);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T8_00,
                DayOfWeek = DayOfWeek.Thursday,
                Parity = Parity.OddWeek,
            });
            b.Course(pachetOptional);
            b.Teacher(i_bat);
            b.Room(room237_4);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T9_45,
                DayOfWeek = DayOfWeek.Thursday,
            });
            b.Course(modelareMatematica);
            b.Teacher(i_bat);
            b.Room(room214_4);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T11_30,
                DayOfWeek = DayOfWeek.Friday,
            });
            b.Course(pachetOptional);
            b.Teacher(gh_rusu);
            b.Room(room122_4);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T13_15,
                DayOfWeek = DayOfWeek.Friday,
            });
            b.Course(pachetOptional);
            b.Teacher(p_sarbu);
            b.Room(room214_4);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T15_00,
                DayOfWeek = DayOfWeek.Friday,
                Parity = Parity.OddWeek,
            });
            b.Course(pachetOptional);
            b.Teacher(p_sarbu);
            b.Room(room214_4);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T15_00,
                DayOfWeek = DayOfWeek.Friday,
                Parity = Parity.EvenWeek,
            });
            b.Course(pachetOptional);
            b.Teacher(i_verlan);
            b.Room(room237_4);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T16_45,
                DayOfWeek = DayOfWeek.Friday,
                Parity = Parity.EveryWeek,
            });
            b.Course(pachetOptional);
            b.Teacher(i_verlan);
            b.Room(room419_4);
        });
    });

    _ = schedule.Scope(s =>
    {
        s.Group(i2201);

        var cloudPrepelita = s.Scope(s1 =>
        {
            s1.Course(cloudComputing);
            s1.Teacher(a_prepelita);
            s1.DayOfWeek(DayOfWeek.Monday);
        });
        cloudPrepelita.RegularLesson(l =>
        {
            l.TimeSlot(timeConfig.T11_30);
            l.Room(room218_4);
            l.Type(LessonType.Curs);
        });
        cloudPrepelita.RegularLesson(l =>
        {
            l.TimeSlot(timeConfig.T13_15);
            l.Room(room218_4a);
            l.Type(LessonType.Lab);
        });
        cloudPrepelita.RegularLesson(l =>
        {
            l.TimeSlot(timeConfig.T15_00);
            l.Room(room218_4a);
            l.Type(LessonType.Lab);
        });
var javaAnghelov = s.Scope(s1 =>
        {
            s1.Course(java);
            s1.Teacher(i_anghelov);
            s1.DayOfWeek(DayOfWeek.Tuesday);
        });
        javaAnghelov.RegularLesson(l =>
        {
            l.TimeSlot(timeConfig.T13_15);
            l.Room(room404_4);
            l.Type(LessonType.Curs);
        });
        javaAnghelov.RegularLesson(l =>
        {
            l.TimeSlot(timeConfig.T15_00);
            l.Room(room326_4);
            l.Type(LessonType.Lab);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T9_45,
                DayOfWeek = DayOfWeek.Tuesday,
            });
            b.Course(grafica);
            b.Teacher(m_cristei);
            b.Room(room326_4);
            b.Type(LessonType.Lab);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T11_30,
                DayOfWeek = DayOfWeek.Wednesday,
            });
            b.Course(aplicatiiWeb);
            b.Teacher(n_plesca);
            b.Room(room254_4);
            b.Type(LessonType.Lab);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T13_15,
                DayOfWeek = DayOfWeek.Wednesday,
            });
            b.Course(computeAlgebra);
            b.Teacher(v_ungureanu);
            b.Room(room145_4);
            b.Type(LessonType.Lab);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T13_15,
                DayOfWeek = DayOfWeek.Thursday,
            });
            b.Course(psi);
            b.Teacher(m_butnaru);
            b.Room(room216a_4a);
            b.Type(LessonType.Lab);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T11_30,
                DayOfWeek = DayOfWeek.Friday,
                Parity = Parity.EvenWeek,
            });
            b.Course(java);
            b.Teacher(i_anghelov);
            b.Room(room254_4);
            b.Type(LessonType.Lab);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T11_30,
                DayOfWeek = DayOfWeek.Friday,
                Parity = Parity.EvenWeek,
            });
            b.Course(java);
            b.Teacher(i_anghelov);
            b.Room(room254_4);
            b.Type(LessonType.Lab);
        });

        _ = s.Scope(w =>
        {
            w.Course(aplicatiiWeb);
            w.DayOfWeek(DayOfWeek.Friday);
            w.Teacher(n_plesca);

            w.RegularLesson(b =>
            {
                b.Type(LessonType.Curs);
                b.TimeSlot(timeConfig.T15_00);
                b.Room(room401_4);
            });

            w.RegularLesson(b =>
            {
                b.Type(LessonType.Lab);
                b.TimeSlot(timeConfig.T16_45);
                b.Room(room251_4);
                b.Parity(Parity.OddWeek);
            });
        });
    });

    _ = schedule.Scope(s =>
    {
        s.Group(ia2201);

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T11_30,
                DayOfWeek = DayOfWeek.Monday,
            });
            b.Course(elaborareAplicatiiGrafice);
            b.Teacher(m_cristei);
            b.Room(room145_4);
            b.Type(LessonType.Lab);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T13_15,
                DayOfWeek = DayOfWeek.Monday,
            });
            b.Course(computeAlgebra);
            b.Teacher(v_ungureanu);
            b.Room(room145_4);
            b.Type(LessonType.Lab);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T9_45,
                DayOfWeek = DayOfWeek.Monday,
            });
            b.Course(grafica);
            b.Teacher(m_cristei);
            b.Room(room326_4);
            b.Type(LessonType.Lab);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T13_15,
                DayOfWeek = DayOfWeek.Tuesday,
            });
            b.Course(webSecurity);
            b.Teacher(v_visnevschi);
            b.Room(room423_4);
            b.Type(LessonType.Lab);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                Parity = Parity.EvenWeek,
                TimeSlot = timeConfig.T13_15,
                DayOfWeek = DayOfWeek.Tuesday,
            });
            b.Course(framework);
            b.Teacher(n_nartea);
            b.Type(LessonType.Lab);
            b.Room(room143_4);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                Parity = Parity.OddWeek,
                TimeSlot = timeConfig.T9_45,
                DayOfWeek = DayOfWeek.Tuesday,
            });
            b.Course(webSecurity);
            b.Teacher(v_visnevschi);
            b.Type(LessonType.Lab);
            b.Room(room145_4);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T13_15,
                DayOfWeek = DayOfWeek.Wednesday,
            });
            b.Course(framework);
            b.Teacher(n_nartea);
            b.Type(LessonType.Curs);
            b.Room(room350_4);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T9_45,
                DayOfWeek = DayOfWeek.Wednesday,
            });
            b.Course(elaborareAplicatiiGrafice);
            b.Type(LessonType.Lab);
            b.Teacher(m_cristei);
            b.Room(room326_4);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T11_30,
                DayOfWeek = DayOfWeek.Thursday,
            });
            b.Course(psi);
            b.Type(LessonType.Lab);
            b.Teacher(m_butnaru);
            b.Room(room222_4);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T9_45,
                DayOfWeek = DayOfWeek.Thursday,
            });
            b.Course(elaborareAplicatiiGrafice);
            b.Type(LessonType.Curs);
            b.Teacher(m_cristei);
            b.Room(room113_4);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T15_00,
                DayOfWeek = DayOfWeek.Friday,
            });
            b.Course(framework);
            b.Type(LessonType.Curs);
            b.Teacher(n_nartea);
            b.Room(room213a_4);
        });

    });

    _ = schedule.Scope(s =>
    {
        s.Groups([i2201, ia2201]);

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T11_30,
                DayOfWeek = DayOfWeek.Tuesday,
            });
            b.Course(grafica);
            b.Teacher(m_cristei);
            b.Room(room401_4);
            b.Type(LessonType.Curs);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T15_00,
                DayOfWeek = DayOfWeek.Thursday,
            });
            b.Course(computeAlgebra);
            b.Type(LessonType.Curs);
            b.Teacher(v_uncureanu);
            b.Room(room213a_4);
        });

        s.RegularLesson(b =>
        {
            b.Date(new()
            {
                TimeSlot = timeConfig.T13_15,
                DayOfWeek = DayOfWeek.Friday,
            });
            b.Course(psi);
            b.Type(LessonType.Curs);
            b.Teacher(n_plesca);
            b.Room(room401_4);
        });
    });
});

var filteredSchedule = schedule.Filter(new()
{
    Grade = 3,
    QualificationType = QualificationType.Licenta,
});
var dayNameProvider = new DayNameProvider();
var timeSlotDisplayHandler = new TimeSlotDisplayHandler();
var generator = new ScheduleTableDocument(filteredSchedule, new()
{
    DayNameProvider = dayNameProvider,
    LessonTimeConfig = timeConfig,
    ParityDisplay = new(),
    StringBuilder = new(),
    LessonTypeDisplay = new(),
    TimeSlotDisplay = timeSlotDisplayHandler,
    SubGroupNumberDisplay = new(),
});

QuestPDF.Settings.License = LicenseType.Community;
QuestPDF.Settings.EnableDebugging = true;
generator.GeneratePdfAndShow();

