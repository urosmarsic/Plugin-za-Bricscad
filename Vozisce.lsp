; Optional loader: add this directory to BricsCAD support paths.
(vl-load-com)
(setq vr:dll (findfile "bin/v1.4/Vozisce.dll"))
(if (not vr:dll) (setq vr:dll (findfile "Vozisce.dll")))
(if (not vr:dll) (setq vr:dll (findfile "bin/Vozisce.dll")))
(if vr:dll
  (command "_.NETLOAD" vr:dll)
  (princ "\nVozisce.dll ni najdena. Najprej izvedi build.ps1 in NETLOAD bin/v1.4/Vozisce.dll.")
)
(princ)

