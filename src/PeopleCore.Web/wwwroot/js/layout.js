// Reports viewport breakpoint changes to the layout so the sidebar can pick the
// right collapse behaviour (an icon rail on desktop, an off-canvas drawer on
// mobile) instead of guessing from CSS alone.
window.peopleCoreLayout = (function () {
    const watchers = new Map();

    return {
        watchBreakpoint: function (id, query, dotNetRef) {
            const mq = window.matchMedia(query);
            const handler = () => dotNetRef.invokeMethodAsync('OnBreakpointChanged', mq.matches);
            mq.addEventListener('change', handler);
            watchers.set(id, { mq, handler });
            return mq.matches;
        },
        unwatchBreakpoint: function (id) {
            const watcher = watchers.get(id);
            if (watcher) {
                watcher.mq.removeEventListener('change', watcher.handler);
                watchers.delete(id);
            }
        }
    };
})();
