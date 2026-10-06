import { Switch } from "@/components/ui-kits/switch/switch";

type InstallScriptsToggleProps = {
  checked: boolean;
  onChange: (checked: boolean) => void;
};

/**
 * "Allow package install scripts": shown under package.json. Off by default — dependencies install
 * with npm's --ignore-scripts. Native packages (bcrypt, sharp, …) need it on to compile or fetch
 * their binary. The install runs in the same isolated sandbox as the function; a runner can still
 * refuse it. Changes the build, so it takes effect from the next test run or deploy.
 */
export const InstallScriptsToggle = ({ checked, onChange }: InstallScriptsToggleProps) => (
  <div className="flex shrink-0 items-start justify-between gap-3 border-t px-4 py-2.5">
    <div className="flex min-w-0 flex-col gap-0.5">
      <span className="text-xs font-semibold">Allow package install scripts</span>
      <span className="text-xs leading-relaxed text-medium-emphasis">
        Needed for native packages such as bcrypt or sharp. Scripts run in the same isolated
        sandbox as the function. Applies from the next test run or deploy.
      </span>
    </div>
    <Switch
      aria-label="Allow package install scripts"
      checked={checked}
      onCheckedChange={onChange}
      className="flex-shrink-0"
    />
  </div>
);
