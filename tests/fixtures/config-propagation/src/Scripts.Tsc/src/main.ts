namespace FxTsc {
    export function onLoad(): void {
        const message = buildMessage("fx_tsc");
        console.log(message);
    }

    function buildMessage(name: string): string {
        return `${name} loaded`;
    }
}
