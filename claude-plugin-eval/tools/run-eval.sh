#!/usr/bin/env bash
# Packs the fixture and runs the suite with the settings the results were compared under.
# Run it inside WSL from any directory; extra arguments go to `claude plugin eval`, for example --case migrate --runs 1.
# The target is the repository root: plugin eval loads only plugins under its target, and the suite sits beside the plugin rather than inside it so a run cannot browse from the skill into the graders.
set -euo pipefail
suite="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# The Bash sandbox refuses to start when a PATH directory cannot be listed, and WSL appends Windows directories that cannot.
# A plain Linux PATH also keeps runs from calling Windows programs.
PATH="$(dirname "$(command -v claude)"):/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"
export PATH

# Each run gets an empty home, so the subscription login saved under ~/.claude does not reach it; a token from `claude setup-token` does.
token_file="${EVAL_TOKEN_FILE:-$HOME/.claude-eval-token}"
if [[ -z ${CLAUDE_CODE_OAUTH_TOKEN:-} && -r $token_file ]]; then
    CLAUDE_CODE_OAUTH_TOKEN="$(< "$token_file")"
    export CLAUDE_CODE_OAUTH_TOKEN
fi
if [[ -z ${CLAUDE_CODE_OAUTH_TOKEN:-} && -z ${ANTHROPIC_API_KEY:-} ]]; then
    echo "No credential for the eval runs: set CLAUDE_CODE_OAUTH_TOKEN, or save the output of 'claude setup-token' to $token_file." >&2
    exit 1
fi
bash "$suite/tools/pack-fixture.sh"
cd "$suite/.."
evals="$(basename "$suite")/evals"
claude plugin eval . --eval-dir "$evals" --scaffold --trust-plugin --no-publish \
    --ablation "${EVAL_ABLATION:-with-without}" \
    --model "${EVAL_MODEL:-claude-sonnet-5-5}" \
    --runs "${EVAL_RUNS:-3}" \
    --max-cost-usd "${EVAL_MAX_COST_USD:-40}" \
    "$@" \
    --tag "${EVAL_TAG:-core}" \
    --allow-tools Edit Write Bash \
    --json "$evals/results/latest.json"
