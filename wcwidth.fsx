#r "nuget: FSharp.Control.Reactive"
#r "nuget: FSharp.Data"
#r "nuget: System.Reactive.Linq"
#r "nuget: Avalonia"

#load "common.fs"
#load "getopt.fs"
#load "config.fs"
#load "log.fs"
#load "def.fs"
#load "wcwidth.fs"

(* a few symbols from jdhao, wbthomason
01 ●
02 ✔
03 🗙 <--- a surrogate pair
04 ➤
05 ＊
06 ＋
07 ～
08 ⚠
09 ☰ (U+2630)
10 Ꞩ (U+A7A8)
11 Ɇ (U+0246)
12 ⎇ (U+2387)
13 ☲ (U+2632)
*)

open FVim.def
open FVim.wcwidth

let parse = (|Rune|_|) >> Option.get
let codepoint (x: Rune) = sprintf "U+%X" x.Codepoint

let rune_03 = "🗙" |> parse
let rune_04 = "➤" |> parse
let rune_10 = "Ꞩ" |> parse
let rune_12 = "⎇" |> parse

wswidth rune_03 // narrow
wswidth rune_04 // narrow
wswidth rune_10 // narrow
wswidth rune_12 // narrow

codepoint rune_03
codepoint rune_04
codepoint rune_10
codepoint rune_12

// the Miscellaneous Technical plane...

let rune_2300 = "⌀" |> parse
let rune_2301 = "⌁" |> parse
let rune_2302 = "⌂" |> parse
let rune_2303 = "⌃" |> parse
let rune_2304 = "⌄" |> parse
let rune_2305 = "⌅" |> parse
let rune_2306 = "⌆" |> parse
let rune_2307 = "⌇" |> parse
let rune_2308 = "⌈" |> parse
let rune_2309 = "⌉" |> parse
let rune_230A = "⌊" |> parse
let rune_230B = "⌋" |> parse
let rune_230C = "⌌" |> parse
let rune_230D = "⌍" |> parse
let rune_230E = "⌎" |> parse
let rune_230F = "⌏" |> parse
let rune_2310 = "⌐" |> parse
let rune_2311 = "⌑" |> parse
let rune_2312 = "⌒" |> parse
let rune_2313 = "⌓" |> parse
let rune_2314 = "⌔" |> parse
let rune_2315 = "⌕" |> parse
let rune_2316 = "⌖" |> parse
let rune_2317 = "⌗" |> parse
let rune_2318 = "⌘" |> parse
let rune_2319 = "⌙" |> parse
let rune_231A = "⌚" |> parse
let rune_231B = "⌛" |> parse
let rune_231C = "⌜" |> parse
let rune_231D = "⌝" |> parse
let rune_231E = "⌞" |> parse
let rune_231F = "⌟" |> parse
let rune_2320 = "⌠" |> parse
let rune_2321 = "⌡" |> parse
let rune_2322 = "⌢" |> parse
let rune_2323 = "⌣" |> parse
let rune_2324 = "⌤" |> parse
let rune_2325 = "⌥" |> parse
let rune_2326 = "⌦" |> parse
let rune_2327 = "⌧" |> parse
let rune_2328 = "⌨" |> parse
let rune_2329 = "〈" |> parse
let rune_232A = "〉" |> parse
let rune_232B = "⌫" |> parse
let rune_232C = "⌬" |> parse
let rune_232D = "⌭" |> parse
let rune_232E = "⌮" |> parse
let rune_232F = "⌯" |> parse
let rune_2330 = "⌰" |> parse
let rune_2331 = "⌱" |> parse
let rune_2332 = "⌲" |> parse
let rune_2333 = "⌳" |> parse
let rune_2334 = "⌴" |> parse
let rune_2335 = "⌵" |> parse
let rune_2336 = "⌶" |> parse
let rune_2337 = "⌷" |> parse
let rune_2338 = "⌸" |> parse
let rune_2339 = "⌹" |> parse
let rune_233A = "⌺" |> parse
let rune_233B = "⌻" |> parse
let rune_233C = "⌼" |> parse
let rune_233D = "⌽" |> parse
let rune_233E = "⌾" |> parse
let rune_233F = "⌿" |> parse
let rune_2340 = "⍀" |> parse
let rune_2341 = "⍁" |> parse
let rune_2342 = "⍂" |> parse
let rune_2343 = "⍃" |> parse
let rune_2344 = "⍄" |> parse
let rune_2345 = "⍅" |> parse
let rune_2346 = "⍆" |> parse
let rune_2347 = "⍇" |> parse
let rune_2348 = "⍈" |> parse
let rune_2349 = "⍉" |> parse
let rune_234A = "⍊" |> parse
let rune_234B = "⍋" |> parse
let rune_234C = "⍌" |> parse
let rune_234D = "⍍" |> parse
let rune_234E = "⍎" |> parse
let rune_234F = "⍏" |> parse
let rune_2350 = "⍐" |> parse
let rune_2351 = "⍑" |> parse
let rune_2352 = "⍒" |> parse
let rune_2353 = "⍓" |> parse
let rune_2354 = "⍔" |> parse
let rune_2355 = "⍕" |> parse
let rune_2356 = "⍖" |> parse
let rune_2357 = "⍗" |> parse
let rune_2358 = "⍘" |> parse
let rune_2359 = "⍙" |> parse
let rune_235A = "⍚" |> parse
let rune_235B = "⍛" |> parse
let rune_235C = "⍜" |> parse
let rune_235D = "⍝" |> parse
let rune_235E = "⍞" |> parse
let rune_235F = "⍟" |> parse
let rune_2360 = "⍠" |> parse
let rune_2361 = "⍡" |> parse
let rune_2362 = "⍢" |> parse
let rune_2363 = "⍣" |> parse
let rune_2364 = "⍤" |> parse
let rune_2365 = "⍥" |> parse
let rune_2366 = "⍦" |> parse
let rune_2367 = "⍧" |> parse
let rune_2368 = "⍨" |> parse
let rune_2369 = "⍩" |> parse
let rune_236A = "⍪" |> parse
let rune_236B = "⍫" |> parse
let rune_236C = "⍬" |> parse
let rune_236D = "⍭" |> parse
let rune_236E = "⍮" |> parse
let rune_236F = "⍯" |> parse
let rune_2370 = "⍰" |> parse
let rune_2371 = "⍱" |> parse
let rune_2372 = "⍲" |> parse
let rune_2373 = "⍳" |> parse
let rune_2374 = "⍴" |> parse
let rune_2375 = "⍵" |> parse
let rune_2376 = "⍶" |> parse
let rune_2377 = "⍷" |> parse
let rune_2378 = "⍸" |> parse
let rune_2379 = "⍹" |> parse
let rune_237A = "⍺" |> parse
let rune_237B = "⍻" |> parse
let rune_237C = "⍼" |> parse
let rune_237D = "⍽" |> parse
let rune_237E = "⍾" |> parse
let rune_237F = "⍿" |> parse
let rune_2380 = "⎀" |> parse
let rune_2381 = "⎁" |> parse
let rune_2382 = "⎂" |> parse
let rune_2383 = "⎃" |> parse
let rune_2384 = "⎄" |> parse
let rune_2385 = "⎅" |> parse
let rune_2386 = "⎆" |> parse
let rune_2387 = "⎇" |> parse
let rune_2388 = "⎈" |> parse
let rune_2389 = "⎉" |> parse
let rune_238A = "⎊" |> parse
let rune_238B = "⎋" |> parse
let rune_238C = "⎌" |> parse
let rune_238D = "⎍" |> parse
let rune_238E = "⎎" |> parse
let rune_238F = "⎏" |> parse
let rune_2390 = "⎐" |> parse
let rune_2391 = "⎑" |> parse
let rune_2392 = "⎒" |> parse
let rune_2393 = "⎓" |> parse
let rune_2394 = "⎔" |> parse
let rune_2395 = "⎕" |> parse
let rune_2396 = "⎖" |> parse
let rune_2397 = "⎗" |> parse
let rune_2398 = "⎘" |> parse
let rune_2399 = "⎙" |> parse
let rune_239A = "⎚" |> parse
let rune_239B = "⎛" |> parse
let rune_239C = "⎜" |> parse
let rune_239D = "⎝" |> parse
let rune_239E = "⎞" |> parse
let rune_239F = "⎟" |> parse
let rune_23A0 = "⎠" |> parse
let rune_23A1 = "⎡" |> parse
let rune_23A2 = "⎢" |> parse
let rune_23A3 = "⎣" |> parse
let rune_23A4 = "⎤" |> parse
let rune_23A5 = "⎥" |> parse
let rune_23A6 = "⎦" |> parse
let rune_23A7 = "⎧" |> parse
let rune_23A8 = "⎨" |> parse
let rune_23A9 = "⎩" |> parse
let rune_23AA = "⎪" |> parse
let rune_23AB = "⎫" |> parse
let rune_23AC = "⎬" |> parse
let rune_23AD = "⎭" |> parse
let rune_23AE = "⎮" |> parse
let rune_23AF = "⎯" |> parse
let rune_23B0 = "⎰" |> parse
let rune_23B1 = "⎱" |> parse
let rune_23B2 = "⎲" |> parse
let rune_23B3 = "⎳" |> parse
let rune_23B4 = "⎴" |> parse
let rune_23B5 = "⎵" |> parse
let rune_23B6 = "⎶" |> parse
let rune_23B7 = "⎷" |> parse
let rune_23B8 = "⎸" |> parse
let rune_23B9 = "⎹" |> parse
let rune_23BA = "⎺" |> parse
let rune_23BB = "⎻" |> parse
let rune_23BC = "⎼" |> parse
let rune_23BD = "⎽" |> parse
let rune_23BE = "⎾" |> parse
let rune_23BF = "⎿" |> parse
let rune_23C0 = "⏀" |> parse
let rune_23C1 = "⏁" |> parse
let rune_23C2 = "⏂" |> parse
let rune_23C3 = "⏃" |> parse
let rune_23C4 = "⏄" |> parse
let rune_23C5 = "⏅" |> parse
let rune_23C6 = "⏆" |> parse
let rune_23C7 = "⏇" |> parse
let rune_23C8 = "⏈" |> parse
let rune_23C9 = "⏉" |> parse
let rune_23CA = "⏊" |> parse
let rune_23CB = "⏋" |> parse
let rune_23CC = "⏌" |> parse
let rune_23CD = "⏍" |> parse
let rune_23CE = "⏎" |> parse
let rune_23CF = "⏏" |> parse
let rune_23D0 = "⏐" |> parse
let rune_23D1 = "⏑" |> parse
let rune_23D2 = "⏒" |> parse
let rune_23D3 = "⏓" |> parse
let rune_23D4 = "⏔" |> parse
let rune_23D5 = "⏕" |> parse
let rune_23D6 = "⏖" |> parse
let rune_23D7 = "⏗" |> parse
let rune_23D8 = "⏘" |> parse
let rune_23D9 = "⏙" |> parse
let rune_23DA = "⏚" |> parse
let rune_23DB = "⏛" |> parse
let rune_23DC = "⏜" |> parse
let rune_23DD = "⏝" |> parse
let rune_23DE = "⏞" |> parse
let rune_23DF = "⏟" |> parse
let rune_23E0 = "⏠" |> parse
let rune_23E1 = "⏡" |> parse
let rune_23E2 = "⏢" |> parse
let rune_23E3 = "⏣" |> parse
let rune_23E4 = "⏤" |> parse
let rune_23E5 = "⏥" |> parse
let rune_23E6 = "⏦" |> parse
let rune_23E7 = "⏧" |> parse
let rune_23E8 = "⏨" |> parse
let rune_23E9 = "⏩" |> parse
let rune_23EA = "⏪" |> parse
let rune_23EB = "⏫" |> parse
let rune_23EC = "⏬" |> parse
let rune_23ED = "⏭" |> parse
let rune_23EE = "⏮" |> parse
let rune_23EF = "⏯" |> parse
let rune_23F0 = "⏰" |> parse
let rune_23F1 = "⏱" |> parse
let rune_23F2 = "⏲" |> parse
let rune_23F3 = "⏳" |> parse
let rune_23F4 = "⏴" |> parse
let rune_23F5 = "⏵" |> parse
let rune_23F6 = "⏶" |> parse
let rune_23F7 = "⏷" |> parse
let rune_23F8 = "⏸" |> parse
let rune_23F9 = "⏹" |> parse
let rune_23FA = "⏺" |> parse

// The Optical Character Recognition plane.. lol

let rune_2440 = "⑀" |> parse
let rune_2441 = "⑁" |> parse
let rune_2442 = "⑂" |> parse
let rune_2443 = "⑃" |> parse
let rune_2444 = "⑄" |> parse
let rune_2445 = "⑅" |> parse
let rune_2446 = "⑆" |> parse
let rune_2447 = "⑇" |> parse
let rune_2448 = "⑈" |> parse
let rune_2449 = "⑉" |> parse
let rune_244A = "⑊" |> parse


