(function () {
    const mq = window.matchMedia('(prefers-color-scheme: dark)');

    function applyTheme(mode) {
        const pref = mode ?? localStorage.getItem('gitorch-theme');
        const isDark = pref === 'dark' || (pref !== 'light' && mq.matches);
        document.documentElement.classList.toggle('dark', isDark);
        document.documentElement.style.colorScheme = isDark ? 'dark' : 'light';
    }

    applyTheme();
    if (typeof mq.addEventListener === 'function') {
        mq.addEventListener('change', () => {
            if (!localStorage.getItem('gitorch-theme')) {
                applyTheme();
            }
        });
    }

    window.gitorchUi = {
        reconnect: async (btn) => {
            const modal = document.getElementById('components-reconnect-modal');
            const isRejectedOrFailed = modal && (
                modal.classList.contains('components-reconnect-rejected') ||
                modal.classList.contains('components-reconnect-failed')
            );
            if (btn) {
                btn.disabled = true;
                btn.textContent = 'Reconnecting…';
            }
            if (isRejectedOrFailed || !window.Blazor || typeof window.Blazor.reconnect !== 'function') {
                location.reload();
                return;
            }
            try {
                const ok = await window.Blazor.reconnect();
                if (!ok) {
                    location.reload();
                }
            } catch {
                location.reload();
            } finally {
                if (btn) {
                    btn.disabled = false;
                    btn.textContent = 'Reconnect';
                }
            }
        },
        getThemePreference: () => localStorage.getItem('gitorch-theme') || 'system',
        setThemePreference: (mode) => {
            if (mode === 'light' || mode === 'dark') {
                localStorage.setItem('gitorch-theme', mode);
            } else {
                localStorage.removeItem('gitorch-theme');
            }
            applyTheme(mode);
            return mode;
        },
        copyText: async (text) => {
            await navigator.clipboard.writeText(text);
            return true;
        },
        downloadLog: (filename, text) => {
            const blob = new Blob([text], { type: 'text/plain;charset=utf-8' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = filename;
            document.body.appendChild(a);
            a.click();
            a.remove();
            URL.revokeObjectURL(url);
        },
        scrollToBottom: (el, force) => {
            if (!el) return true;
            const near = (el.scrollHeight - el.scrollTop - el.clientHeight) < 56;
            if (force || near) {
                el.scrollTop = el.scrollHeight;
                return true;
            }
            return false;
        },
        isNearBottom: (el) => {
            if (!el) return true;
            return (el.scrollHeight - el.scrollTop - el.clientHeight) < 56;
        },
        focusSelector: (selector) => {
            const el = document.querySelector(selector);
            if (el && typeof el.focus === 'function') {
                el.focus();
            }
        }
    };
})();

