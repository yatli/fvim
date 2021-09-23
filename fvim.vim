let g:clipboard = {
  \ 'name': 'FVimClipboard',
  \ 'copy': {
  \   '+': {lines, regtype -> rpcrequest(g:fvim_channel, 'set-clipboard', lines, regtype)},
  \   '*': {lines, regtype -> rpcrequest(g:fvim_channel, 'set-clipboard', lines, regtype)},
  \ },
  \ 'paste': {
  \   '+': {-> rpcrequest(g:fvim_channel, 'get-clipboard')},
  \ '*': {-> rpcrequest(g:fvim_channel, 'get-clipboard')},
  \ }
\ }

command! -nargs=0 FVimDetach call rpcnotify(g:fvim_channel, 'remote.detach')
command! -nargs=0 FVimToggleFullScreen call rpcnotify(g:fvim_channel, 'ToggleFullScreen')

command! -complete=expression -nargs=1 FVimCursorSmoothMove call rpcnotify(g:fvim_channel, 'cursor.smoothmove', <args>)
command! -complete=expression -nargs=1 FVimCursorSmoothBlink call rpcnotify(g:fvim_channel, 'cursor.smoothblink', <args>)
command! -complete=expression -nargs=1 FVimFontLineHeight call rpcnotify(g:fvim_channel, 'font.lineheight', <args>)
command! -complete=expression -nargs=1 FVimFontAutoSnap call rpcnotify(g:fvim_channel, 'font.autosnap', <args>)
command! -complete=expression -nargs=1 FVimFontAntialias call rpcnotify(g:fvim_channel, 'font.antialias', <args>)
command! -complete=expression -nargs=1 FVimFontLigature call rpcnotify(g:fvim_channel, 'font.ligature', <args>)
command! -complete=expression -nargs=1 FVimFontDrawBounds call rpcnotify(g:fvim_channel, 'font.drawBounds', <args>)
command! -complete=expression -nargs=1 FVimFontAutohint call rpcnotify(g:fvim_channel, 'font.autohint', <args>)
command! -complete=expression -nargs=1 FVimFontSubpixel call rpcnotify(g:fvim_channel, 'font.subpixel', <args>)
command! -complete=expression -nargs=1 FVimFontHintLevel call rpcnotify(g:fvim_channel, 'font.hintLevel', <args>)
command! -complete=expression -nargs=1 FVimFontNormalWeight call rpcnotify(g:fvim_channel, 'font.weight.normal', <args>)
command! -complete=expression -nargs=1 FVimFontBoldWeight call rpcnotify(g:fvim_channel, 'font.weight.bold', <args>)
command! -complete=expression -nargs=1 FVimFontNoBuiltinSymbols call rpcnotify(g:fvim_channel, 'font.nonerd', <args>)
command! -complete=expression -nargs=1 FVimKeyDisableShiftSpace call rpcnotify(g:fvim_channel, 'key.disableShiftSpace', <args>)
command! -complete=expression -nargs=1 FVimKeyAutoIme call rpcnotify(g:fvim_channel, 'key.autoIme', <args>)

" let! _ = nvim.``command!`` -complete=expression FVimUIMultiGrid 1 call rpcnotify(g:fvim_channel, 'ui.multigrid', <args>)
command! -complete=expression -nargs=1 FVimUIPopupMenu call rpcnotify(g:fvim_channel, 'ui.popupmenu', <args>)
" let! _ = nvim.``command!`` -complete=expression FVimUITabLine 1 call rpcnotify(g:fvim_channel, 'ui.tabline', <args>)
" let! _ = nvim.``command!`` -complete=expression FVimUICmdLine 1 call rpcnotify(g:fvim_channel, 'ui.cmdline', <args>)
command! -complete=expression -nargs=1 FVimUIWildMenu call rpcnotify(g:fvim_channel, 'ui.wildmenu', <args>)
" let! _ = nvim.``command!`` -complete=expression FVimUIMessages 1 call rpcnotify(g:fvim_channel, 'ui.messages', <args>)
" let! _ = nvim.``command!`` -complete=expression FVimUITermColors 1 call rpcnotify(g:fvim_channel, 'ui.termcolors', <args>)
" let! _ = nvim.``command!`` -complete=expression FVimUIHlState 1 call rpcnotify(g:fvim_channel, 'ui.hlstate', <args>)

command! -complete=expression -nargs=1 FVimDrawFPS call rpcnotify(g:fvim_channel, 'DrawFPS', <args>)
command! -complete=expression -nargs=1 FVimCustomTitleBar call rpcnotify(g:fvim_channel, 'CustomTitleBar', <args>)

command! -complete=expression -nargs=1 FVimBackgroundOpacity call rpcnotify(g:fvim_channel, 'background.opacity', <args>)
command! -complete=expression -nargs=1 FVimBackgroundComposition call rpcnotify(g:fvim_channel, 'background.composition', <args>)
command! -complete=expression -nargs=1 FVimBackgroundAltOpacity call rpcnotify(g:fvim_channel, 'background.altopacity', <args>)
command! -complete=expression -nargs=1 FVimBackgroundImage call rpcnotify(g:fvim_channel, 'background.image.file', <args>)
command! -complete=expression -nargs=1 FVimBackgroundImageOpacity call rpcnotify(g:fvim_channel, 'background.image.opacity', <args>)
command! -complete=expression -nargs=1 FVimBackgroundImageStretch call rpcnotify(g:fvim_channel, 'background.image.stretch', <args>)
command! -complete=expression -nargs=1 FVimBackgroundImageHAlign call rpcnotify(g:fvim_channel, 'background.image.halign', <args>)
command! -complete=expression -nargs=1 FVimBackgroundImageVAlign call rpcnotify(g:fvim_channel, 'background.image.valign', <args>)

function! s:fvim_on_bufwinenter()
  let l:bufnr=expand("<abuf>")
  let l:wins=win_findbuf(l:bufnr)
  call rpcnotify(g:fvim_channel, 'OnBufWinEnter', l:bufnr, l:wins)
endfunction

function! s:fvim_on_winenter()
  let l:win=nvim_get_current_win()
  let l:bufnr=nvim_win_get_buf(l:win)
  let l:wins=win_findbuf(l:bufnr)
  call rpcnotify(g:fvim_channel, 'OnBufWinEnter', l:bufnr, l:wins)
endfunction

function! s:fvim_on_cursorhold()
  let l:bufnr=nvim_get_current_buf()
  let l:signs=sign_getplaced(l:bufnr, {'group': '*'})
  call rpcnotify(g:fvim_channel, 'OnSignUpdate', l:bufnr, l:signs)
endfunction

function! s:fvim_on_vim_enter()
  runtime! ginit.vim
  call rpcnotify(g:fvim_channel, 'OnInitComplete')
endfunction

lua <<EOF
fvim_test_gui = function()
  local gui = require('gui-widgets')
  local w1 = gui.put_file("F:/test/push_1.png","image/png")
  local w2 = gui.put_file("F:/test/push_2.png","image/png")
  gui.place(w1, 0, 0, 0, 8, 2, {
    ['clicked-widget']=w2;
    ['clicked-exec']='silent call VsimToggleColor()';
    ['released-widget']=w1;
    ['halign']='center';
    ['valign']='center';
    ['stretch']='uniform';
    ['hide']='cursor';
  })

  local w3 = gui.put_data("Hello Text","text/plain")
  gui.place(w3, 0, 2, 0, 20, 2, {
    ['text-font']='Arial';
    ['text-scale']=2;
    ['text-hlid']='Normal';
    ['hide']='cursorline';
  })

  local w4 = gui.put_file("F:/test/5.svg", "image/svg")
  gui.place(w4, 0, 4, 0, 8, 2, {
    ['halign']='center';
    ['valign']='center';
    ['stretch']='uniform';
    ['hide']='cursor';
    ['svg-themed']=true;
  })

  local w5 = gui.put_file("F:/test/7.svg", "image/svg")
  gui.place(w5, 0, 6, 0, 8, 1, {
    ['halign']='center';
    ['valign']='center';
    ['stretch']='uniform';
    ['hide']='cursor';
    ['svg-themed']=true;
  })
  gui.update_view(0)
end
EOF

augroup FVim
  autocmd BufWinEnter * call s:fvim_on_bufwinenter()
  autocmd WinEnter * call s:fvim_on_winenter()
  autocmd WinNew * call s:fvim_on_winenter()
  autocmd WinScrolled * call s:fvim_on_winenter()
  autocmd CursorHold * call s:fvim_on_cursorhold()
  autocmd CursorHoldI * call s:fvim_on_cursorhold()
augroup END

" trigger upon VimEnter
if v:vim_did_enter 
  call s:fvim_on_vim_enter()
else 
  autocmd VimEnter * call s:fvim_on_vim_enter()
endif
