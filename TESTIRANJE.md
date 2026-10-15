# Preverjanje različice 1.4

Preverjeno 15. 10. 2026: prevod za BricsCAD V24, 120 preverjanj jedra ter preizkusi WinForms predogleda, CSV, spremembe parametrov, spremembe osi R100 → R200 in neveljavnih podatkov. Artefakti so v bin/v1.4.

Ločen zagon BricsCAD z /automation /B ni dosegel testnega ukaza in ni ustvaril poročila. Testni proces je bil ustavljen. CAD integracija, vključno s sprotnim izrisom in UNDO, ni potrjena.

## Ponovljiv prevod in testi

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -CadVersion 24 -OutputDirectory bin\v1.4 -BuildCadTests
```

Jedro preverja dvojno parabolo na četrtinah prehoda, osnovno geometrijo zunaj prehoda, točen radij in središče odmika krožnega loka, desne krivine, prekratke elemente in shranjevanje nastavitev. Vmesnik preverja samodejni preračun parametrov, novo os, invalidacijo in preprečitev zanke ob napaki.

## Preizkus podatkovne baze v BricsCAD

1. Če je stara Vozisce.dll naložena, znova zaženi BricsCAD.
2. NETLOAD: bin/v1.4/Vozisce.dll, nato bin/v1.4/CadSmoke.dll.
3. Zaženi VR_CAD_TESTS. Ukaz uporablja ločeno začasno podatkovno bazo in ne spreminja trenutne risbe.
4. Preveri bin/v1.4/cad-test-results.txt. Test preverja ustvarjene robove, krožne odseke, geometrijo in besedilo kote, shranjevanje nastavitev ter odstranjevanje samo izrisa izbrane osi. Uspeh je potrjen šele, ko poročilo vsebuje PASS.

## Ročni preizkus interaktivnega urejanja

1. VR_PRIMER, nato VR_RAZSIRITEV in izberi ustvarjeno os. Risba mora biti v metrih.
2. SI, širina 6 m, tovornjak–avto, notranja stran: največja razširitev 0,400 m, širina pasov 6,400 m. Robna pasova sta dodatno po 0,25 m.
3. Izračunaj in izriši. Pred s=40 m je širitev nič; pri s=50 m je 0,050 m, pri s=60 m 0,200 m, pri s=70 m 0,350 m. Pri s=80 m doseže 0,400 m. Na loku širina ostane stalna. Notranji rob pasov mora imeti R=96,6 m, zunanji R=103 m.
4. Vključi/izključi Oznake in Kote. Kota meri dodatno širino od osnovnega roba pasov do razširjenega roba; pri simetrični razširitvi sta dve koti po 0,200 m. Pri ravni osi ni kote z ničelno vrednostjo.
5. Z odprtim oknom spremeni osnovno širino ali vozila. Po približno 350 ms se izris zamenja; prejšnji robovi, oznake in kote ne ostanejo. Os in izrisi drugih osi ostanejo.
6. Uredi os z ročaji oziroma PEDIT; posodobitev nastopi po koncu ukaza. Za R=200 m pričakuj 0,200 m. Pri spremembi geometrije ponovno preveri predloge prehodnic.
7. Naredi netangentni lom ali vnesi preozek vozni pas. Izris se odstrani in poročilo pokaže napako. Odprava neveljavnega vnosa omogoči novo posodobitev. Zaklenjen sloj lahko prepreči odstranitev; preveri prikazano napako.
8. Preizkusi levi/desni zavoj, simetrično in zunanjo izbiro ter istosmerne in nasprotnosmerne loke. Tesnejši lok se ne sme zožiti; odmiki nasprotnih strani se ne odštevajo.
9. UNDO/REDO mora ohraniti svoje spremembe; sprotno posodabljanje se ustavi. Za nadaljevanje ponovno izračunaj in izriši.
10. Zapri okno, shrani in odpri DWG. Ponovi VR_RAZSIRITEV na isti osi: nastavitve se obnovijo, izris se zamenja. Po zaprtju okna ni sprotnega spremljanja osi.
11. Preizkusi INSUNITS=4, R≤20 m za SI in R≤30 m za RAS/Tirol: neveljaven izris mora biti zavrnjen. Preveri CSV, temno/svetlo temo in povečan DPI.
12. Na sloju VR_OSNOVNO_VOZISCE preveri črtkana osnovna robova. V primeru prema–lok–prema morata imeti vsak po štiri temena; srednji segment mora biti ARC. Nerazširjeni zunanji rob ima prav tako štiri temena. Na prehodih razširjenega roba ostanejo vzorčena temena; začetna nerazširjena prema je en segment od s=0 do s=40 m.

## Cestna os in 2D POLYLINE

1. VR_OS: določi začetno točko in smer prve preme. Preveri, da je entiteta POLYLINE tipa 2D Simple in da se odpre zavihek Cestna os pred zavihkom širin.
2. V tabeli sestavi premo L=80, klotoido L=40/R0=0/R1=100, lok L=80/R=100, klotoido L=40/R0=100/R1=0 in premo L=40. Skupna analitična dolžina je 280 m. Na prvi klotoidi pri s=90 m je k=0,0025 1/m. Krožni del mora imeti bulge, klotoidi pa vzorčena temena.
3. Preveri tangentni poligon ter oznake Z/K na začetku in koncu vseh petih elementov. Spreminjanje L/R in vlečenje začetka v predogledu morata zamenjati izris brez podvojenih oznak.
4. Nariši dva ARC ali 2D poliliniji z enim krožnim odsekom. Izberi Izberi dva loka v risbi, določi smeri in preveri tangentno klotoido. Pri neizvedljivi povezavi mora biti prikazana napaka; izvorna loka in zadnja veljavna cestna os ostanejo.
5. Premakni izvorni fiksni lok z CAD ukazom. Ob odprtem oknu se povezava ponovno reši ali prikaže napako. Sprememba parametrov loka v tabeli prekine povezavo z izvorno entiteto.
6. Preizkusi isto ureditev na LWPOLYLINE in POLYLINE 2D. Urejanje mora ohraniti tip in identiteto osi; pri klasični 2D POLYLINE se morajo spremljati tudi spremembe objektov VERTEX. Z in predznačeni bulge morajo ostati pravilni. POLYLINE 3D in Fit/Spline se zavrnejo.
7. Zapri, shrani in ponovno odpri DWG. Urejevalnik mora obnoviti elemente in njihove povezave iz VR_ALIGNMENT. Podprt premik posameznega temena med odprtim urejevalnikom mora ohraniti matematično definicijo; poljuben zunanji poseg zunaj urejevalnika jo lahko razveljavi.
8. V ločenem zavihku nastavi levi pas 3,25 m in desni 3,75 m: vsota mora biti 7 m, osnovna robova pa na ustreznih različnih odmikih. Preozek posamezni pas se zavrne glede na pripadajoče vozilo.

Automatizirano jedro vključuje 30 preverjanj sestavljanja, tangentnosti, ohranitve središč fiksnih lokov, nasprotnosmerne povezave, neizvedljivih povezav, ločenih pasov in shranjevanja definicije. Koordinate klotoide so primerjane z neodvisno Simpsonovo integracijo. Skupno je 120 preverjanj; testi vmesnika dodatno preverjajo nove zavihke, sestavljanje osi in neveljaven osnutek.

VR_CAD_TESTS vključuje še ustvarjanje in posodabljanje obeh tipov polilinije, obnovitev analitične klotoide iz Xrecord, višino osi, tangentni poligon in oznake brez podvojitev. Ta CAD preverjanja so prevedena, vendar v trenutnem okolju še niso izvedena.

## Ročaji in preverjanje stikov (1.4)

1. Odpri VR_PRIMER in VR_RAZSIRITEV. V odprtem urejevalniku preveri križne bloke VR_OS_ROCAJI; v predogledu naj bodo vidna krajišča, središče loka, točka oblike in smerne točke.
2. Premakni konec začetne preme. Krožni lok in zaključna prema naj sledita novi tangenti; G0/G1 morata ostati ustrezna. Enako preveri s premikom temena same POLYLINE 2D in na LWPOLYLINE.
3. Premakni sredinsko točko loka v predogledu in kot križni blok v risbi. Preveri novi radij/dolžino, tangentni priključek, osvežen tangentni poligon, krajišča, robove in razširitev.
4. Premakni središče priključenega loka tako, da nastane vrzel. Izpis mora vključevati razmik in odklon tangent; neveljavna os se ne potrdi. V CAD se ročaji in os obnovijo. Premik središča samostojnega loka mora ostati mogoč.
5. Na osi prema–klotoida–lok premakni konec ali sredinsko točko klotoide. Dolžina in končna ukrivljenost se preračunata; prilepljeni lok sledi. Preveri G0/G1 in ujemanje ukrivljenosti. Ponovi s premikom enega temena klotoide v risbi: definicija klotoide se ne sme izgubiti.
6. Pri dveh fiksnih lokih in prosti klotoidi premikaj središčna/smerna ročaja lokov; reševalnik naj najde novo povezavo ali varno zavrne premik. Prosta klotoida nima neodvisnih ročajev priključnih mest.
7. Preveri premik celotne osi z MOVE in zavrnitev neenoličnega premika več posameznih temen. Preveri preklican premik, UNDO/REDO, ponoven izračun, zapiranje okna in ponovno odprtje iste osi.

28 novih preverjanj jedra pokriva meritve G0/G1, krajišča, spremembo radija, prestavitev samostojnega loka, prilepljene elemente, ohranitev klotoide po premiku temena, translacijo, bulge in zavrnjene premike. Skupno je 120 preverjanj. Preizkus WinForms preveri tudi objavo veljavnega premika in ohranitev geometrije pri zavrnitvi. Native CAD testi dodatno vsebujejo ustvarjanje in branje podatkov značilnih ročajev; ti so prevedeni in še čakajo na izvedbo v BricsCAD.

Dolžine brez prehodnice ostajajo projektni predlog ali ročni vnos. Dvojna parabola je profil spremembe širine; spremenljivi rob je vzorčen z izbranim korakom. Za normativno potrditev je potreben pregled uporabljene izdaje smernic.
