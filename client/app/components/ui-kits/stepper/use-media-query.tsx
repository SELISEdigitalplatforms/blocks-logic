import * as React from "react";

export function useMediaQuery(query: string) {
	const [value, setValue] = React.useState(false);

	React.useEffect(() => {
		function onChange(event: MediaQueryListEvent) {
			setValue(event.matches);
		}

		const result = matchMedia(query);
		result.addEventListener("change", onChange);
		// Initial sync from the external matchMedia source after mount. Moving this into
		// render (or useSyncExternalStore) would change the first-render value from false.
		// eslint-disable-next-line react-hooks/set-state-in-effect
		setValue(result.matches);

		return () => result.removeEventListener("change", onChange);
	}, [query]);

	return value;
}
