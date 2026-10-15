# Razširitev vozišča za BricsCAD

## Popravek interaktivnega urejanja 1.4.1

Prevedeni dodatek za BricsCAD V24 je `bin/v1.4.1/Vozisce.dll`. Po ponovnem zagonu BricsCAD ga naloži z NETLOAD. V oknu VR_RAZSIRITEV klikni **Premakni vozlišče v risbi** ali zaženi **VR_OS_VOZLISCE**. Klikni blizu želenega značilnega ročaja, preveri izpis izbranega ročaja v ukazni vrstici in določi novo lego. ESC prekliče izbiro. Ukaz preveri geometrijo pred spremembo in takoj osveži prikaz. Brez odprtega urejevalnika najprej zahteva izbiro osi. Neizvedljiv premik se zavrne z razlogom v ukazni vrstici.

Obdelava CAD dogodkov preskoči začasne objekte brez identitete in objekte drugih podatkovnih baz. Po obdelavi sistemskega premika temena ali pomožnega bloka se prikaz izrecno regenerira. Vzrok težave s sistemskimi ročaji še ni potrjen v BricsCAD; novi ukaz omogoča neposreden premik brez odvisnosti od teh dogodkov. Uspešno je izvedenih 120 preverjanj jedra in preizkusi WinForms; novi CAD ukaz je preveden, interaktivno preverjanje v BricsCAD še ni izvedeno.

Razvojna različica 1.4. Izvorna koda dodatka je v tej mapi; obstoječi projekt CestnaOs ni spremenjen.

## Stanje in omejitve

Dne 15. 10. 2026 je bila zgrajena bin/v1.4/Vozisce.dll za nameščeni BricsCAD V24 z njegovima BrxMgd.dll in TD_Mgd.dll. Uspešnih je 120 preverjanj jedra ter preizkusi WinForms izračuna, upodobitve, CSV, interaktivnih parametrov, spremembe osi in neveljavnih vhodov. Ločen avtomatiziran zagon BricsCAD ni dosegel CAD testnega ukaza; NETLOAD, izris kot in robov ter UNDO še zahtevajo preverjanje v BricsCAD. Datoteke v bin z imeni CoreTests, UiSmoke in UI-check niso dodatek za NETLOAD.

Slovenski izračun in javno objavljena formula po RAS-L/Tirol sta implementirana. Dolžine prehodov BREZ prehodnice so projektni predlog ali ročni vnos; avtomatska določitev po celotnih RAS smernicah še ni implementirana. Za to je potreben preverjen izsek iz uporabljene izdaje RAS-L, vključno s pravili za lok–prema, istosmerna loka in nasprotnosmerna loka. Program rezultata ne označuje kot normativno potrjenega.

## Prevod in namestitev

Za BricsCAD V24/V25 na Windows x64, Pro ali višji, uporabi .NET Framework 4.8. V26 uporablja .NET 8 in zahteva ustrezen .NET 8 SDK za gradnjo. Pot nastavi na svojo namestitev:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -CadVersion 24 -OutputDirectory bin\v1.4 -BuildCadTests -BricsCADDir "C:\Program Files\Bricsys\BricsCAD V24 en_US"
~~~

Za V26 zamenjaj številko in mapo:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -CadVersion 26 -BricsCADDir "C:\Program Files\Bricsys\BricsCAD V26 en_US"
~~~

Skript najprej preveri jedro. Za V26 potrebuje tudi .NET Framework 4.8 za te teste. Uporablja lokalna BrxMgd.dll in TD_Mgd.dll; sistemskih BricsCAD knjižnic ne kopira v paket.

Če je stara različica že naložena, najprej znova zaženi BricsCAD. Nato izvedi NETLOAD in izberi bin\v1.4\Vozisce.dll. Zaženi VR_RAZSIRITEV. Po želji dodaj Vozisce.lsp v APPLOAD / Startup Suite; mapa dodatka mora biti na podporni poti. Upoštevaj nastavitve zaupanja vrednih poti svojega BricsCAD.

## Uporaba

Za novo cestno os zaženi VR_OS ter izberi začetno točko in točko za smer prve preme. Nastane klasična 2D POLYLINE. VR_RAZSIRITEV sprejme obstoječo 2D POLYLINE ali LWPOLYLINE; ob urejanju ohrani njeno obliko entitete in identiteto. POLYLINE mora biti tipa Simple; 3D polilinije in Fit/Spline različice niso podprte.

Zavihki so razporejeni: Cestna os → Vozišče in vozni pasovi → Razširitev in vozila → Prehodi razširitve → Rezultati → Poročilo. V zavihku širin sta levi in desni pas urejena ločeno. Skupna širina je njuna vsota; urejanje skupne širine sorazmerno spremeni oba pasova.

1. Izberi odprto 2D POLYLINE ali LWPOLYLINE v WCS XY. Enota koordinat mora biti meter; INSUNITS drugih znanih enot se zavrne. Pri INSUNITS=0 uporabnik zagotovi, da je enota meter. Os je ločnica levega in desnega osnovnega voznega pasu, ne rob vozišča.
2. Vnesi skupno osnovno širino dveh voznih pasov, robni pas na vsaki strani, začetno stacionažo, vozili in stran razširitve. Levi in desni pas sta lahko različno široka. Več kot dva vozna pasova v tej različici ni podprto.
3. Izberi slovensko ali RAS-L/Tirol metodo. Vozili sta izbrani za srečevanje; vsaka prispeva svojo razširitev.
4. V zavihku Prehodnice preveri samodejne predloge. Meje so relativna dolžina od začetka osi. Potrdi z Uporabi; znano prehodnico lahko dodaš ročno. k=1/R pomeni levo krivino, k=-1/R desno, k=0 premo. Program preveri približno ujemanje z geometrijo.
5. Za prehode brez prehodnice vnesi preverjeno projektno dolžino ali uporabi označen predlog iz naklona roba. Hitrost je podatek poročila, ne argument tega predloga.
6. Izračunaj / predogled pokaže robove in tabelo. CSV vsebuje parametre, vire, potrjene prehodnice, stacionaže in izračunane širine. Izriši v risbo zamenja prejšnji izris iste osi na slojih VR_…. Možnosti Oznake in Kote omogočata oznako največje razširitve ter koti dodatne širine na levem/desnem robu. Kote prikazujejo razširitev od osnovnega roba, brez robnega pasu.

Okno dopušča urejanje osi v risbi. Po prvem izrisu sprememba parametrov samodejno preračuna in zamenja prejšnje robove, kote in oznake. Sprememba osi sproži posodobitev po koncu CAD ukaza, ne med vlečenjem ročaja. Izvorna os in izrisi drugih osi ostanejo. Ob neveljavni osi ali parametrih se zastareli izris odstrani, razen če odstranitev prepreči zaklenjen sloj; napaka je prikazana v poročilu. Sprotno posodabljanje deluje, dokler je okno odprto. Po UNDO/REDO se ustavi; za nadaljevanje ponovno izračunaj in izriši. Parametri, potrjene prehodnice in geometrija se shranijo v VR_RAZSIRITVE; ponovni ukaz na isti osi obnovi nastavitve. Če se geometrija spremeni, potrditve prehodnic zahtevajo nov pregled.

VR_PRIMER ustvari os s premo, lokom R=100 m in premo. Pri SI in srečevanju tovornjak–avto je največja razširitev 0,400 m; pri dveh vlačilcih 1,000 m. VR_POMOC izpiše pomoč.

Črtkana osnovna robova pasov sta na sloju VR_OSNOVNO_VOZISCE in prikazujeta osnovno širino brez razširitve ter brez dodatnih robnih pasov. Ustvarjena sta tudi v predogledu in se pri spremembah zamenjata skupaj z drugimi robovi. Vzorec VR_CRTKANO vsebuje črto 1 m in presledek 0,5 m; njegov prikaz upošteva nastavitve merila tipov črt v risbi. Na nerazširjenem delu črtkani in polni rob sovpadata.

Na vhodni osi s pravimi krožnimi elementi so odseki s stalno širino združeni: ena prema ali en krožni lok na element polilinije, brez vmesnih vzorčenih temen. Primer prema–lok–prema ima na nerazširjenem robu in črtkani osnovi štiri temena. Temena dvojne parabole ostanejo zaradi spreminjanja širine. Pri osi, podani kot tetive, program ne rekonstruira izvornega krožnega loka.

## Pomen predloga dolžine iz naklona roba

Naklon i je največja sprememba bočnega odmika roba na meter dolžine cestne osi. Vrednost 0,02 m/m pomeni 2 cm/m. To ni prečni sklon vozišča in ne vzdolžni naklon nivelete.

Za dvojno parabolo program uporabi L=max(10 m, 2Δb/i). Če se en rob premakne za 0,40 m in je i=0,02, je prehod dolg 40 m. Pri simetrični skupni razširitvi 0,40 m se vsak rob premakne za 0,20 m, zato je predlog 20 m. Manjši i pomeni daljši prehod. Največji naklon je na sredini prehoda; na koncih je sprememba odmika tangentna osnovnemu robu.

Izklop možnosti aktivira ročno dolžino. Predlog velja za preskoke širine brez potrjene prehodnice; potrjena prehodnica uporablja svoje meje. Če sosednji element ni dovolj dolg, program prehod omeji in opozori, da zahtevani naklon ni zagotovljen. Predlog je geometrijski projektni izračun, ne normativno potrjena RAS dolžina.

## Vozila, formule in prehodnice

### Sestavljanje cestne osi

### Interaktivni ročaji in kontrola stikov (1.4)

Odpri VR_RAZSIRITEV oziroma VR_OS in pusti urejevalnik odprt. Ob odprtju se pripravijo pomožni ročaji na sloju VR_OS_ROCAJI. V risbi so to križni bloki: izberi posamezen blok in premakni njegovo vstavišče z ročajem ali z MOVE. V predogledu so krajišča in točke oblike rdeče, središča lokov oranžna, smerne točke tangent zelene; opis se prikaže ob zadržanju miške nad točko.

Krajišče začetne preme določa njeno dolžino in smer; prilepljeni elementi ji sledijo. Krajišče priključene preme se projicira na njeno začetno tangento, da se priključek ohrani. Sredinska točka ali konec loka spremeni radij in dolžino ob ohranitvi začetne tangente. Središčni ročaj prestavi lok z istim radijem; če fiksni sosednji priključek tega ne dopušča, je premik zavrnjen. Premik konca ali točke oblike klotoide numerično spremeni njeno dolžino in končno ukrivljenost; prilepljeni lok sledi novi ukrivljenosti. Prosto klotoido med fiksnima lokoma urejaj prek ročajev fiksnih lokov, saj njeni priključni mesti določi reševalnik.

Podprt je tudi premik posameznega temena same POLYLINE 2D ali LWPOLYLINE. Na definirani klotoidi se po premiku ponovno sestavi matematična klotoida in ostala temena; premaknjena točka ne ostane poljuben lom. Premik celotne polilinije je podprt kot translacija. Za več neodvisnih hkratnih premikov ali spremembo števila temen uporabi urejevalnik elementov; tak poseg ni enolična sprememba definirane osi.

Po spremembi se ločeno preveri zveznost G0 (razmik krajišč ≤ 0,00001 m) in tangentnost G1 (odklon ≤ 0,000001 rad, približno 0,0000573°). Pri klotoidi se preveri tudi ujemanje ukrivljenosti s sosedom (≤ 0,00000001 1/m). Poročilo označi številki neustreznih elementov, dejanski razmik in odklon tangent. Neveljaven premik v predogledu ni objavljen v os; pri neveljavnem CAD premiku se os in ročaji obnovijo iz zadnje veljavne definicije. Omejitve fiksnih elementov se ne prezrejo.

V predogledu je odziv sproten, prenos v risbo sledi po kratkem zamiku. CAD premik se preveri po koncu ukaza, ne med samim vlečenjem sistemskega ročaja. Samodejno spremljanje deluje samo med odprtim urejevalnikom; po zaprtju ga ponovno zaženi z VR_RAZSIRITEV. Odprtje pripravi pomožne entitete, tudi če vozišča nato ne izrišeš. Po UNDO/REDO se sprotno urejanje ustavi do ponovnega izračuna in izrisa.

### Parametri sestavljanja

V prvem zavihku dodaj premo, krožni lok ali klotoido. Povezava Prilepi prevzame krajišče in tangento predhodnega elementa. Dolžina je v metrih. Predznačen radij je pozitiven za levi in negativen za desni zavoj. Pri klotoidi R0/R1 določita začetno in končno ukrivljenost; R=0 pomeni premo. Klotoida mora imeti različni končni ukrivljenosti in se mora z ukrivljenostjo ujemati s sosednjim elementom. Elementa prema in krožni lok smeta imeti preskok ukrivljenosti; prehod širine ostaja dvojna parabola.

Za povezavo dveh obstoječih lokov izberi Izberi dva loka v risbi. Sprejeti so ARC, 2D POLYLINE ali LWPOLYLINE z enim krožnim odsekom. Za vsak lok določi smer Naprej/Obrni. Alternativno v tabeli postavi dva zaporedna fiksna krožna loka ter izberi prvega in gumb Klotoida med fiksna loka. Parametri fiksnega loka so začetni X/Y, smer začetne tangente, radij in dolžina. L v vrstici proste klotoide je začetna ocena za reševanje; dejanska rešena dolžina je v sporočilu pod predogledom.

Povezava numerično poišče tangentni priključni mesti in dolžino klotoide. Središči in radija obeh lokov ostaneta fiksna; priključka sta omejena na obstoječa loka. Izvorna izbrana loka se ne odrežeta: odrezana odseka sta del sestavljene osi. Pri neizvedljivi povezavi je prikazana napaka, zadnja veljavna cestna os pa ostane. Metoda podpira tudi nasprotnosmerna loka. Ne omogoča vseh vrst prostih skupin spiral–prema–spirala iz Civil 3D.

Spremembe v tabeli ali vlečenje začetne točke/fiksnega krajišča v predogledu po kratkem zamiku posodobijo os v risbi. Izbrana izvorna fiksna loka se spremljata med odprtim urejevalnikom; po koncu njunega CAD urejanja se povezava znova reši. Ročna sprememba parametrov uvoženega loka v tabeli prekine njegovo povezavo z izvorno entiteto. Preme in krožni loki so pravi odseki polilinije; klotoide so vzorčene z izbranim korakom, izračuni ukrivljenosti in tangent pa uporabljajo definirano matematično krivuljo. Definicija je shranjena v razširitvenem slovarju osi VR_ALIGNMENT in se ob ponovnem odpiranju obnovi, če se polilinija ni zunaj urejevalnika spremenila.

Tangentni poligon je na sloju VR_TANGENTNI_POLIGON. Oznake začetkov in koncev so na VR_OS_KRAJISCA: P = prema, L = krožni lok, K = klotoida, Z/K = začetek/konec; s je relativna dolžina od začetka osi. Pri vzporednih tangentah S-klotoide se uporabi tudi srednja tangenta. Sestavljanje fiksnih, prilepljenih in prostih elementov uporablja podoben osnovni potek kot [urejanje osi Civil 3D](https://help.autodesk.com/cloudhelp/2021/ENU/Civil3D-UserGuide/files/GUID-39D91C0F-FA46-4AF4-BCFD-ED96156371AF.htm).

Lop je medosna razdalja s sprednjim previsom; D je reducirana dolžina za izbrani RAS/Tirol model. Nista ista parameter. Dejanske medosne razdalje posameznega tovornjaka ali sklopljenega vozila predpis o največjih merah ne določa; uporabiti je treba homologacijo ali podatke proizvajalca. Katalog zato ne izmišljuje teh ločenih razdalj. Tipske širine in dolžine izhajajo iz cestnega pravilnika, ne iz največjih dovoljenih mer. Ročne vrednosti so sledljive v poročilu.

SI: razširitev posameznega pasu Lop²/(2R). RAS-L prek Tirol: R−√(R²−D²) za vsako vozilo. R je ocenjen polmer osi; to ni simulacija zavijalne površine vozila. Pri SI R≤20 m ter RAS/Tirol R≤30 m je izris zavrnjen in potrebna je analiza sledi vozila. Reduciranje pri kratkih lokih in prag opustitve razširitve v Tirol postopku nista vključena; izračun ohranja polno vrednost.

Polilinija s krožnimi odseki omogoča točno razpoznavo radija in smeri. Polilinija iz tetiv uporablja oceno ukrivljenosti iz treh sosednjih točk; lomi nad 5° se zavrnejo. Sprememba ukrivljenosti, približno linearna glede na dolžino osi, ustvari predlog prehodnice. To ni dokaz, da je izvorna krivulja klotoida. Potrebna je dovolj gosta, kakovostna polilinija.

Na potrjeni prehodnici širina preide po dvojni paraboli med končnima profiloma: f(t)=2t² za t≤0,5 in f(t)=1−2(1−t)² za t≥0,5, pri t=(s−s0)/L. Na obeh koncih je naklon spremembe širine nič. Predlog dolžine je L=max(10 m, 2Δb/i), kjer je i največji naklon spremembe odmika. Na S-prehodnici se razširitev premika med levim in desnim robom. Pri preskoku ukrivljenosti brez prehodnice se predlagana razširitev na bolj tesnem loku ohrani v celoti, prehod pa se raztegne v sosednji element. Prekrivajoče se razširitve uporabljajo večji odmik na vsaki strani. To je konservativna projektna konstrukcija, ne potrjena implementacija vseh RAS pravil. Prehod je omejen na neposredno sosednji element; če je prekratek za izbrano dolžino, poročilo opozori na potrebo po podaljšanju elementa ali prilagoditvi dolžine.

Izris ohranja natančne preme in koncentrične krožne odseke s stalno širino. Dvojna parabola na spremenljivo širokem delu je vzorčena polilinija; korak določa približek, ne CAD tolerance. Ne vključuje vitoperjenja, nivelete, zaznavanja konfliktov z objekti ali simulacije srečevanja. Kontrola zazna samokrižanje vzorčenih robov in približevanje notranjega roba središču krivine.

## Viri

- [Pravilnik o projektiranju cest, objava 2005](https://www.uradni-list.si/glasilo-uradni-list-rs/vsebina/2005-01-3896): 8. in 24. člen.
- [PISRS – prečiščeno besedilo](https://pisrs.si/api/datoteke/integracije/351752075): označeno kot prenehalo veljati; uporabnost in morebitno nadaljnjo uporabo po prehodnih določbah mora projektant preveriti za konkretni projekt.
- [Pravilnik o delih in opremi vozil, objava 2022](https://www.uradni-list.si/glasilo-uradni-list-rs/vsebina/2022-01-0296): Priloga 1 določa največje dovoljene mere. To ni katalog dejanskih medosnih razdalj.
- [Amt der Tiroler Landesregierung, Fahrbahnverbreiterung, 2003](https://www.tirol.gv.at/fileadmin/themen/verkehr/service/downloads/Leitfaden_Fahrbahnverbreiterung__Stand_01.10.2003_.pdf): javna obdelava RAS-L formule in D. Avstrijsko navodilo ni neposredno celotno nemško besedilo RAS.
- [Nemško uvedbeno pismo RAL](https://fgsv-verlag.de/pub/media/pdf/201.r.16052013.pdf): nadomestitev starejših smernic, vključno z RAS-L.
- [Bricsys .NET API](https://developer.bricsys.com/bricscad/help/en_US/CurVer/DevRef/source/dotNETAPI.htm): potrebne knjižnice in razlika med V25 ter V26.

## Preverjanje

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -TestsOnly
~~~

tests/CoreTests.cs preverja formule, levi/desni zavoj, simetrijo, preskoke širine, nasprotnosmerna loka, ocenjene prehodnice, stacionažo, neveljavne podatke in križanja. tests/UiSmoke.cs preveri izračun iz vmesnika in shrani upodobitev in CSV v izbrano izhodno mapo. Še potreben: preizkus NETLOAD, izrisa, CSV, zapiranja okna in UNDO v ciljnem BricsCAD.

